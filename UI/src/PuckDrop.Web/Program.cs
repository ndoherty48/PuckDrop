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

// Register PuckDropApiClient with the API Gateway base URL
var apiBaseUrl = builder.Configuration["ApiClientSettings:BaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/puckdrop/") });
builder.Services.AddScoped<PuckDropApiClient>();

var app = builder.Build();

// WebAssembly does not support IHostedService, so TelemetryHostedService is never started.
_ = app.Services.GetService<MeterProvider>();
_ = app.Services.GetService<TracerProvider>();

await app.RunAsync();
