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
// The API Gateway emulator uses fixed ports configured in the AppHost.
var apiBaseUrl = builder.Configuration["services:api-gateway:http:0"]
    ?? builder.Configuration["ApiClientSettings:BaseUrl"]
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

// Config is deliberately provider-agnostic (standard OIDC authorization-code-flow settings
// work identically against Cognito or Keycloak's own discovery/token endpoints - there's no
// per-provider divergence at the protocol level the way there is server-side, see
// PuckDrop.Api.ApiServiceCollectionExtensions). Switching providers is just pointing
// wwwroot/appsettings.json's "Oidc" section at a different authority/client ID.
builder.Services.AddOidcAuthentication(options =>
{
    var config = builder.Configuration.GetSection("Oidc");
    options.ProviderOptions.Authority = config["Authority"];
    options.ProviderOptions.ClientId = config["ClientId"];
    options.ProviderOptions.ResponseType = config["ResponseType"];

    options.ProviderOptions.DefaultScopes.Add("openid");
    options.ProviderOptions.DefaultScopes.Add("profile");
}).AddAccountClaimsPrincipalFactory<PuckDropClaimsPrincipalFactory>();

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
