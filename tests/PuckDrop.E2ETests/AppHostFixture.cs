using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests;

/// <summary>
/// Boots the real AppHost and one shared headless browser, once for the whole run.
/// </summary>
public class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan ResourceWaitTimeout = TimeSpan.FromMinutes(3);

    // Well above Playwright's defaults: cold WASM loads against the single-threaded Lambda
    // emulator can be slow without anything being stuck.
    public const float DefaultTimeoutMs = 120_000;

    // How long to watch for the "Couldn't reach the server" page after a navigation. It has to
    // cover WASM boot plus the 10s auth-config timeout; 12s wasn't enough.
    public const float BootstrapFailureWindowMs = 25_000;

    private DistributedApplication _app = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    /// <summary>
    /// Base URL for the Blazor app, which blazor-gateway serves under "/web/".
    /// </summary>
    public Uri BlazorBaseUri { get; private set; } = null!;

    // Program.cs falls back to this dev URL, which only resolves under `aspire start`. The test
    // host uses a random port, so requests to it are rerouted to the real endpoint.
    private const string HardcodedApiGatewayFallback = "http://api-gateway-puckdrop.dev.localhost:8080";
    private Uri _apiGatewayEndpoint = null!;

    // One login per user per run. Tests in the collection run sequentially, so no locking.
    private readonly Dictionary<string, CapturedSession> _sessionCache = new();

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PuckDrop_AppHost>();
        var app = await appHost.BuildAsync();

        await app.StartAsync();

        using var cts = new CancellationTokenSource(ResourceWaitTimeout);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("blazor-gateway", cts.Token);
        // Its health check proves it's routing to the Lambda, which every page load needs.
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api-gateway", cts.Token);

        _app = app;

        var gatewayEndpoint = _app.GetEndpoint("blazor-gateway", "http");
        BlazorBaseUri = new Uri(gatewayEndpoint, "web/");
        _apiGatewayEndpoint = _app.GetEndpoint("api-gateway", "http");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        // Assertions.Expect has its own process-wide timeout, separate from the context's.
        Assertions.SetDefaultExpectTimeout(DefaultTimeoutMs);
    }

    /// <summary>
    /// A fresh, isolated browser context (own cookies/storage) per test.
    /// </summary>
    public Task<IBrowserContext> NewBrowserContextAsync() => NewBrowserContextAsync(storageState: null);

    private async Task<IBrowserContext> NewBrowserContextAsync(string? storageState)
    {
        var options = new BrowserNewContextOptions
        {
            BaseURL = BlazorBaseUri.ToString(),
            // Keycloak's HTTPS endpoint uses a self-signed dev certificate.
            IgnoreHTTPSErrors = true
        };
        if (storageState is not null)
            options.StorageState = storageState;

        var context = await _browser.NewContextAsync(options);
        context.SetDefaultTimeout(DefaultTimeoutMs);
        context.SetDefaultNavigationTimeout(DefaultTimeoutMs);

        await context.RouteAsync($"{HardcodedApiGatewayFallback}/**", async route =>
        {
            var originalUri = new Uri(route.Request.Url);
            var redirected = new Uri(_apiGatewayEndpoint, originalUri.PathAndQuery);
            await route.ContinueAsync(new RouteContinueOptions { Url = redirected.ToString() });
        });

        return context;
    }

    /// <summary>
    /// Like <see cref="NewBrowserContextAsync()"/>, but already signed in with a session from
    /// <see cref="LoginAndCaptureSessionAsync"/>.
    /// </summary>
    /// <remarks>
    /// StorageState restores localStorage (the OIDC user, via persist-login.js) and Keycloak's
    /// session cookie, which lets the app renew silently once the 5-minute access token expires.
    /// sessionStorage, where Blazor caches its auth settings, is seeded by an init script.
    /// </remarks>
    public async Task<IBrowserContext> NewAuthenticatedBrowserContextAsync(CapturedSession session)
    {
        var context = await NewBrowserContextAsync(session.StorageState);

        var json = JsonSerializer.Serialize(session.SessionStorage);
        await context.AddInitScriptAsync($$"""
            (() => {
                const data = {{json}};
                for (const [key, value] of Object.entries(data))
                    window.sessionStorage.setItem(key, value);
            })();
            """);

        return context;
    }

    /// <summary>
    /// Navigates to <paramref name="url"/>, retrying a failed bootstrap - see
    /// <see cref="RetryOnBootstrapFailureAsync"/>.
    /// </summary>
    public Task GotoWithBootstrapRetryAsync(IPage page, string url, int maxAttempts = 4) =>
        RetryOnBootstrapFailureAsync(page, () => page.GotoAsync(url), $"at {url}", maxAttempts);

    /// <summary>
    /// Reloads the current page, retrying a failed bootstrap - for navigations that aren't a
    /// plain <c>GotoAsync</c>, such as the round trip through Keycloak's logout.
    /// </summary>
    public Task ReloadOnBootstrapFailureAsync(IPage page, int maxAttempts = 4) =>
        RetryOnBootstrapFailureAsync(page, () => page.ReloadAsync(), $"after reload at {page.Url}", maxAttempts);

    /// <summary>
    /// The auth-config fetch at boot times out after 10s, and the busy Lambda emulator sometimes
    /// exceeds that, leaving the app on "Couldn't reach the server". Retry rather than change
    /// production behaviour for test-only slowness.
    /// </summary>
    private static async Task RetryOnBootstrapFailureAsync(
        IPage page, Func<Task> attempt, string attemptDescription, int maxAttempts)
    {
        for (var attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
        {
            await attempt();

            try
            {
                // A timeout here means no failure page appeared, so the bootstrap succeeded.
                await page.WaitForSelectorAsync("text=Couldn't reach the server", new PageWaitForSelectorOptions
                {
                    Timeout = BootstrapFailureWindowMs
                });
            }
            catch (TimeoutException)
            {
                return;
            }

            if (attemptNumber == maxAttempts)
                throw new Exception(
                    $"App still showing \"Couldn't reach the server\" after {maxAttempts} attempts {attemptDescription}.");
        }
    }

    /// <summary>
    /// Logs in through Keycloak once per user per run and returns the session.
    /// </summary>
    public async Task<CapturedSession> LoginAndCaptureSessionAsync(string username, string password)
    {
        if (_sessionCache.TryGetValue(username, out var cached))
            return cached;

        await using var context = await NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await GotoWithBootstrapRetryAsync(page, BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));

        await page.FillAsync("#username", username);
        await page.FillAsync("#password", password);
        await page.ClickAsync("#kc-login");

        // "Logout" only renders once the user is authenticated.
        await page.WaitForSelectorAsync("text=Logout");

        var sessionStorage = await page.EvaluateAsync<Dictionary<string, string>>("() => ({ ...sessionStorage })");
        var storageState = await context.StorageStateAsync();
        var session = new CapturedSession(storageState, sessionStorage);
        _sessionCache[username] = session;
        return session;
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

/// <summary>
/// A signed-in browser session - see <see cref="AppHostFixture.NewAuthenticatedBrowserContextAsync"/>.
/// </summary>
public sealed record CapturedSession(string StorageState, IReadOnlyDictionary<string, string> SessionStorage);
