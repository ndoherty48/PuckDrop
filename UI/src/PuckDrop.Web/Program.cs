using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using PuckDrop.Web;
using PuckDrop.Web.Auth;
using PuckDrop.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Configuration.AddEnvironmentVariables();
builder.AddBlazorClientServiceDefaults();

// Register PuckDropApiClient with API Gateway base URL.
// The API Gateway emulator uses fixed ports configured in the AppHost. Neither of those two
// local-dev-only sources can ever resolve for a real deployment - this app is a static bundle
// served from S3/CloudFront, so it can't read AppHost-injected config or env vars at runtime.
//
// builder.HostEnvironment.BaseAddress (Blazor WASM's own origin, always correct regardless of
// deployment target) is the real fallback for that case: CloudFront's /puckdrop/* behavior
// (BlazorStaticSitePublishTarget) forwards same-origin calls straight through to the API
// Gateway, so a relative base URL just works, no CORS, nothing baked in at build time. BUT this
// only holds when the app is served from its own origin's root - locally, blazor-gateway serves
// it under a resource-name sub-path instead (e.g. "https://localhost:PORT/web/"), and that
// sub-path is NOT proxied to the API the way CloudFront's real deployment path is. Using
// BaseAddress there resolved to the gateway's own origin+"/web/", which 404s/falls back to
// index.html for any API call - not a hardcoded-literal problem, a genuine regression this
// caused (confirmed via a real E2E run, not assumed) until this path check was added.
// Root path ("/") means "served from an origin root" (the CloudFront case); anything else means
// "served under a sub-path by something like blazor-gateway" (the local-dev case), where the
// hardcoded literal below is the one that's actually correct.
var baseAddressIsOriginRoot = new Uri(builder.HostEnvironment.BaseAddress).AbsolutePath is "/";
var apiBaseUrl = builder.Configuration["services:api-gateway:http:0"]
    ?? builder.Configuration["ApiClientSettings:BaseUrl"]
    ?? (baseAddressIsOriginRoot ? builder.HostEnvironment.BaseAddress : null)
    ?? "http://api-gateway-puckdrop.dev.localhost:8080";

Console.WriteLine($"[PuckDrop] API base URL: {apiBaseUrl}");

// "/puckdrop" matches LambdaEntryPoint.UsePathBase("/puckdrop"), which the local API Gateway
// emulator route (AppHost.cs) and the production API Gateway route (DeploymentStack) both target.
var baseUri = new Uri(apiBaseUrl.TrimEnd('/') + "/puckdrop/");

// AddHttpMessageHandler<AuthorizationMessageHandler> is what actually attaches the
// "Authorization: Bearer <access_token>" header - every PuckDrop.Api controller
// requires [Authorize], so without this every call 401s regardless of login state.
builder.Services.AddHttpClient<PuckDropApiClient>(client => client.BaseAddress = baseUri)
    .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(authorizedUrls: [apiBaseUrl]));

// OIDC config is fetched from the API at boot rather than baked into wwwroot/appsettings.json -
// the real values (a Cognito User Pool, or local Keycloak) are only known server-side, where
// CDK-provisioned config already flows in via the existing Lambda env-var seam (see
// PuckDrop.Api.ApiServiceCollectionExtensions.AddApis / AppHost.cs's ConstructFunctionCallback).
// A short timeout plus an explicit AuthConfigLoadResult flag (checked by App.razor) keeps the
// app from booting into a blank white page if the API is unreachable at startup.
AuthConfigModel? authConfig = null;
AuthConfigLoadResult authConfigLoadResult;
try
{
    using var bootstrapHttpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(10) };
    authConfig = await bootstrapHttpClient.GetFromJsonAsync<AuthConfigModel>("auth-config");
    authConfigLoadResult = authConfig is not null
        ? AuthConfigLoadResult.Ok
        : new AuthConfigLoadResult(false, "The server returned no auth configuration.");
}
catch (Exception ex)
{
    authConfigLoadResult = new AuthConfigLoadResult(false, $"Could not reach the server: {ex.Message}");
}

builder.Services.AddSingleton(authConfigLoadResult);

if (authConfig is not null)
{
    // Config is deliberately provider-agnostic (standard OIDC authorization-code-flow settings
    // work identically against Cognito or Keycloak's own discovery/token endpoints). The one
    // protocol-level divergence is logout - Cognito's end_session_endpoint needs extra parameters,
    // flagged by auth-config's UseCognitoLogout and applied in MainLayout.Logout, which is why the
    // model itself is registered here. Switching providers is still purely a server-side config
    // change - the UI just reflects whatever auth-config returns.
    builder.Services.AddSingleton(authConfig);
    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = authConfig.Authority;
        options.ProviderOptions.ClientId = authConfig.ClientId;
        options.ProviderOptions.ResponseType = authConfig.ResponseType;

        // RemoteAuthenticationOptions<OidcProviderOptions>.DefaultScopes already comes
        // pre-populated with ["openid", "profile"] out of the box (confirmed directly) - adding
        // them again here duplicated the scope parameter in the real authorize request
        // ("openid profile openid profile"), which Cognito rejected outright with 400 Bad
        // Request (confirmed live, via a real deployed distribution).
    }).AddAccountClaimsPrincipalFactory<PuckDropClaimsPrincipalFactory>();
}
// If the fetch failed, OIDC services are deliberately left unregistered - App.razor checks
// AuthConfigLoadResult before rendering anything that would need them (AuthenticationStateProvider
// etc.), so nothing tries to resolve a service that was never configured.

// Note: unlike ASP.NET Core endpoint routing, Blazor WASM's AuthorizeRouteView does
// NOT consult AuthorizationOptions.FallbackPolicy - a page with no [Authorize]/[AllowAnonymous]
// attribute at all is simply never checked. There is no "secure by default" here, so every
// page needs an explicit @attribute [Authorize] (mirroring PuckDrop.Api, where every
// controller carries a class-level [Authorize]); Authentication.razor and Unauthorized.razor
// are explicitly [AllowAnonymous] so login/logout and the "not authorized" page stay reachable.
builder.Services.AddAuthorizationCore();

var app = builder.Build();

// WebAssembly does not support IHostedService, so TelemetryHostedService is never started.
_ = app.Services.GetService<MeterProvider>();
_ = app.Services.GetService<TracerProvider>();

await app.RunAsync();
