using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests;

/// <summary>
/// Boots the real PuckDrop AppHost (real DynamoDB Local, real Keycloak, the real Lambda-hosted
/// API, and the real Blazor WASM app via blazor-gateway) via Aspire.Hosting.Testing, plus one
/// shared real browser, for the whole test run. Built once (not per test) - shared collection
/// fixture, matching Aspire's own documented pattern for expensive-to-build AppHost instances.
/// </summary>
public class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan ResourceWaitTimeout = TimeSpan.FromMinutes(3);

    private DistributedApplication _app = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    /// <summary>
    /// Base URL for the running Blazor app - note the "/web/" suffix: the app is served under
    /// that path (StaticWebAssetBasePath = "web" in PuckDrop.Web.csproj), not the gateway root.
    /// </summary>
    public Uri BlazorBaseUri { get; private set; } = null!;

    // Program.cs's API base URL resolution falls back to a hardcoded literal
    // ("http://api-gateway-puckdrop.dev.localhost:8080") when Aspire's dynamic service-discovery
    // config isn't readable - which it never is in the browser (confirmed elsewhere this
    // session: Blazor WASM can't read AppHost-injected env vars at runtime). That literal only
    // resolves under `aspire start`/`aspire run`, whose CLI/dashboard process provides the
    // ".dev.localhost" DNS convention and (separately) pins api-gateway's port - neither exists
    // under DistributedApplicationTestingBuilder, which has no CLI process and allocates a real
    // random port instead. Rather than touch that shared Program.cs/AppHost.cs behavior,
    // intercept and redirect requests to the hardcoded literal to the real resolved endpoint,
    // entirely on the test side.
    private const string HardcodedApiGatewayFallback = "http://api-gateway-puckdrop.dev.localhost:8080";
    private Uri _apiGatewayEndpoint = null!;

    // AppHost.cs's "create-table" executable only WaitFor(dynamoDbLocal)'s Running state, not a
    // health check - DynamoDB Local's container can report Running slightly before its actual
    // HTTP listener accepts connections, so create-table sometimes fires too early and fails
    // ("Connection was closed before we received a valid response"), which cascades into "api"
    // never starting. This is the same transient race already known from plain `aspire start`
    // local dev (where a manual restart cycle papers over it) - confirmed here to reproduce
    // consistently under DistributedApplicationTestingBuilder, which has no interactive retry.
    // Retrying the whole boot is the pragmatic fix at the test-fixture level, without touching
    // AppHost.cs's shared local-dev resource wiring.
    private const int MaxStartAttempts = 3;

    public async ValueTask InitializeAsync()
    {
        for (var attempt = 1; attempt <= MaxStartAttempts; attempt++)
        {
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PuckDrop_AppHost>();
            var app = await appHost.BuildAsync();

            try
            {
                await app.StartAsync();

                using var cts = new CancellationTokenSource(ResourceWaitTimeout);
                await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
                await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", cts.Token);
                await app.ResourceNotifications.WaitForResourceHealthyAsync("blazor-gateway", cts.Token);

                _app = app;
                break;
            }
            catch when (attempt < MaxStartAttempts)
            {
                await app.DisposeAsync();
            }
        }

        var gatewayEndpoint = _app.GetEndpoint("blazor-gateway", "http");
        BlazorBaseUri = new Uri(gatewayEndpoint, "web/");
        _apiGatewayEndpoint = _app.GetEndpoint("api-gateway", "http");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>
    /// A fresh, isolated browser context (own cookies/storage) per test - pass a previously
    /// captured storage-state JSON (see the login helper added alongside the first real
    /// auth-dependent tests) to start already authenticated, without repeating the login UI.
    /// </summary>
    public async Task<IBrowserContext> NewBrowserContextAsync(string? storageState = null)
    {
        var options = new BrowserNewContextOptions
        {
            BaseURL = BlazorBaseUri.ToString(),
            // The real OIDC redirect lands on Keycloak's HTTPS endpoint
            // (https://localhost:8543/realms/PuckDrop/...), which uses a self-signed local-dev
            // certificate - Chromium blocks navigation to it by default.
            IgnoreHTTPSErrors = true
        };
        if (storageState is not null)
            options.StorageState = storageState;

        var context = await _browser.NewContextAsync(options);

        await context.RouteAsync($"{HardcodedApiGatewayFallback}/**", async route =>
        {
            var originalUri = new Uri(route.Request.Url);
            var redirected = new Uri(_apiGatewayEndpoint, originalUri.PathAndQuery);
            await route.ContinueAsync(new RouteContinueOptions { Url = redirected.ToString() });
        });

        return context;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class E2ETestCollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "E2E collection";
}
