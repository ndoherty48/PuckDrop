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

// API base URL: Aspire service discovery and config only exist locally. Deployed, the app is
// served from the CloudFront root, which proxies /puckdrop/* to the API, so the app's own origin
// works. Locally blazor-gateway serves it under /web/, which isn't proxied, so fall through to
// the dev default.
var baseAddressIsOriginRoot = new Uri(builder.HostEnvironment.BaseAddress).AbsolutePath is "/";
var apiBaseUrl = builder.Configuration["services:api-gateway:http:0"]
    ?? builder.Configuration["ApiClientSettings:BaseUrl"]
    ?? (baseAddressIsOriginRoot ? builder.HostEnvironment.BaseAddress : null)
    ?? "http://api-gateway-puckdrop.dev.localhost:8080";

Console.WriteLine($"[PuckDrop] API base URL: {apiBaseUrl}");

// Matches LambdaEntryPoint's UsePathBase and both API Gateway routes.
var baseUri = new Uri(apiBaseUrl.TrimEnd('/') + "/puckdrop/");

// Attaches the bearer token to API calls.
builder.Services.AddHttpClient<PuckDropApiClient>(client => client.BaseAddress = baseUri)
    .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(authorizedUrls: [apiBaseUrl]));

// OIDC config comes from the API at boot, since only the server knows the provider. If it fails,
// App.razor shows an error from AuthConfigLoadResult instead of a blank page.
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
    // Registered for MainLayout.Logout, which needs UseCognitoLogout.
    builder.Services.AddSingleton(authConfig);
    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = authConfig.Authority;
        options.ProviderOptions.ClientId = authConfig.ClientId;
        options.ProviderOptions.ResponseType = authConfig.ResponseType;


        // Don't add openid/profile: they're already in DefaultScopes, and duplicates make
        // Cognito reject the authorize request.
    }).AddAccountClaimsPrincipalFactory<PuckDropClaimsPrincipalFactory>();
}
// If the fetch failed, OIDC stays unregistered; App.razor doesn't render anything that needs it.

// AuthorizeRouteView ignores FallbackPolicy, so every page needs an explicit [Authorize] or
// [AllowAnonymous].
builder.Services.AddAuthorizationCore();

builder.Services.AddScoped<ConfirmDialogService>();

var app = builder.Build();

// WebAssembly does not support IHostedService, so TelemetryHostedService is never started.
_ = app.Services.GetService<MeterProvider>();
_ = app.Services.GetService<TracerProvider>();

await app.RunAsync();
