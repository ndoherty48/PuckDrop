using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using PuckDrop.Web;
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

var baseUri = new Uri(apiBaseUrl.TrimEnd('/') + "/puckdrop/");
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = baseUri });
builder.Services.AddScoped<PuckDropApiClient>();

var app = builder.Build();

// WebAssembly does not support IHostedService, so TelemetryHostedService is never started.
_ = app.Services.GetService<MeterProvider>();
_ = app.Services.GetService<TracerProvider>();

await app.RunAsync();
