using System.Text.Json;
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

    // Playwright's own defaults (30s navigation, 5s everything else, incl. Assertions.Expect)
    // assume a normal app under normal load. Here, every page load talks to a real, cold-booting
    // WASM app behind a real AWS Lambda Service Emulator that processes one invocation at a time
    // - under this suite's own sequential real traffic that can genuinely take a while longer than
    // that, not because anything is actually stuck. Set high enough to absorb realistic delay
    // rather than fail on it; a test that's truly stuck will still fail, just slower.
    public const float DefaultTimeoutMs = 120_000;

    // How long to watch for Program.cs's "Couldn't reach the server" page after a navigation
    // before treating the bootstrap as having succeeded - see RetryOnBootstrapFailureAsync.
    public const float BootstrapFailureWindowMs = 25_000;

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

    // Login flow is exercised for real by Browser/LoginTests.cs; everything else that just needs
    // an authenticated session reuses a cached session instead of repeating the login UI. Tests
    // share this collection fixture and run sequentially within it (default xUnit
    // collection-fixture behavior), so no locking is needed around this cache.
    private readonly Dictionary<string, CapturedSession> _sessionCache = new();

    public async ValueTask InitializeAsync()
    {
        // create-table used to fire before DynamoDB Local's HTTP listener was actually ready
        // (AddAWSDynamoDBLocal registered no health check, so its WaitFor only meant "container
        // process running") - fixed at the source in AppHost.cs by giving the dynamodb resource
        // a real "is it accepting connections" health check, so no retry loop is needed here.
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PuckDrop_AppHost>();
        var app = await appHost.BuildAsync();

        await app.StartAsync();

        using var cts = new CancellationTokenSource(ResourceWaitTimeout);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("blazor-gateway", cts.Token);
        // api-gateway now has a real health check (AppHost.cs) proving it's actually routing to
        // the "api" Lambda function, not just that its process is running - wait on it too, since
        // every test's first real HTTP call (the /auth-config bootstrap fetch) goes through it.
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api-gateway", cts.Token);

        _app = app;

        var gatewayEndpoint = _app.GetEndpoint("blazor-gateway", "http");
        BlazorBaseUri = new Uri(gatewayEndpoint, "web/");
        _apiGatewayEndpoint = _app.GetEndpoint("api-gateway", "http");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        // Process-wide, not per-context - Assertions.Expect (used by every ToBeVisibleAsync()
        // call across this suite) has its own separate default timeout, independent of any
        // IBrowserContext/IPage setting.
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
            // The real OIDC redirect lands on Keycloak's HTTPS endpoint
            // (https://localhost:8543/realms/PuckDrop/...), which uses a self-signed local-dev
            // certificate - Chromium blocks navigation to it by default.
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
    /// Like <see cref="NewBrowserContextAsync()"/>, but pre-seeded with a previously captured
    /// session (see <see cref="LoginAndCaptureSessionAsync"/>), so navigating straight to any
    /// page in it starts already authenticated - without repeating the login UI.
    /// </summary>
    /// <remarks>
    /// The signed-in user (including its refresh token) lives in <c>localStorage</c> - moved there
    /// from Blazor's default <c>sessionStorage</c> by <c>wwwroot/js/persist-login.js</c> - and
    /// <see cref="IBrowserContext.StorageStateAsync"/>/<see cref="BrowserNewContextOptions.StorageState"/>
    /// cover localStorage and cookies, so the captured storageState carries the session over.
    /// It also carries Keycloak's own session cookie, which matters when this suite's own slowness
    /// means the access token (5-minute lifespan in the imported realm) has expired by the time a
    /// test uses it: the app then renews silently instead of falling back to a real interactive
    /// login page (confirmed live: an intermittent RoleGatingTests failure showed exactly that
    /// before cookies were carried over). sessionStorage is still seeded too, via a context-level
    /// init script that Playwright runs before any page script on every navigation - Blazor keeps
    /// its cached auth settings there, and it costs nothing to restore.
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
    /// Navigates to <paramref name="url"/>, working around one specific known-transient failure
    /// mode - see <see cref="RetryOnBootstrapFailureAsync"/>.
    /// </summary>
    public Task GotoWithBootstrapRetryAsync(IPage page, string url, int maxAttempts = 4) =>
        RetryOnBootstrapFailureAsync(page, () => page.GotoAsync(url), $"at {url}", maxAttempts);

    /// <summary>
    /// Reloads the current page, working around the same failure mode as
    /// <see cref="GotoWithBootstrapRetryAsync"/> - for a navigation that isn't a plain
    /// <c>GotoAsync</c> call, such as the real cross-origin round trip
    /// NavigationManager.NavigateToLogout makes to Keycloak's own logout endpoint and back
    /// (confirmed live: that round trip re-runs Program.cs's bootstrap fetch on return, so it can
    /// hit the same transient failure a fresh page load can). The browser is already on the
    /// correct post-round-trip URL by the time this is needed, so a plain reload - not repeating
    /// whatever action got here - is enough.
    /// </summary>
    public Task ReloadOnBootstrapFailureAsync(IPage page, int maxAttempts = 4) =>
        RetryOnBootstrapFailureAsync(page, () => page.ReloadAsync(), $"after reload at {page.Url}", maxAttempts);

    /// <summary>
    /// Program.cs's OIDC bootstrap fetch (<c>GET /auth-config</c>) has a deliberate, documented
    /// 10-second timeout, so the app doesn't hang on a blank page if the API is genuinely
    /// unreachable in production. This suite's own concurrent traffic against a Lambda emulator
    /// that only processes one invocation at a time occasionally makes even a valid response take
    /// longer than that, which - correctly, from the app's perspective - trips the same fail-fast
    /// path and lands on its permanent "Couldn't reach the server" error page. Retry
    /// <paramref name="attempt"/> a few times rather than touch that production behavior for this
    /// test-environment-specific slowness.
    /// </summary>
    private static async Task RetryOnBootstrapFailureAsync(
        IPage page, Func<Task> attempt, string attemptDescription, int maxAttempts)
    {
        for (var attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
        {
            await attempt();

            try
            {
                // Give the bootstrap fetch's own 10s timeout room to fail visibly before deciding
                // whether to retry - if this throws (times out), no failure page ever appeared,
                // meaning the bootstrap fetch succeeded and normal navigation/assertions can
                // proceed as usual. The window starts at the page's load event, before the WASM
                // runtime has booted and made that fetch, so it has to cover boot time plus the
                // 10s timeout: 12s wasn't enough (confirmed live - a slow boot put the failure page
                // just past it, and the test then waited out its full timeout on that page).
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
    /// Drives the real Keycloak login UI once for the given user (cached thereafter for this
    /// run) and returns the resulting session, for <see cref="NewAuthenticatedBrowserContextAsync"/>.
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

        // "Logout" only renders in MainLayout's <Authorized> branch - a display-name-agnostic
        // signal that the round trip back from Keycloak completed and the app considers the user
        // authenticated.
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
/// A logged-in browser session captured by <see cref="AppHostFixture.LoginAndCaptureSessionAsync"/>,
/// for <see cref="AppHostFixture.NewAuthenticatedBrowserContextAsync"/> - see that method's remarks
/// for why both parts are needed.
/// </summary>
public sealed record CapturedSession(string StorageState, IReadOnlyDictionary<string, string> SessionStorage);
