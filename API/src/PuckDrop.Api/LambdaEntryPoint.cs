using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using OpenTelemetry.Instrumentation.AWSLambda;
using OpenTelemetry.Trace;
using PuckDrop.Application;
using PuckDrop.Infrastructure;

namespace PuckDrop.Api;

/// <summary>
/// This class extends from APIGatewayProxyFunction which contains the method FunctionHandlerAsync which is the 
/// actual Lambda function entry point. The Lambda handler field should be set to
/// 
/// PuckDrop.Api::PuckDrop.Api.LambdaEntryPoint::FunctionHandlerAsync
/// </summary>
public class LambdaEntryPoint : Amazon.Lambda.AspNetCoreServer.APIGatewayHttpApiV2ProxyFunction
{
    private static TracerProvider? _tracerProvider;

    /// <summary>
    /// The builder has configuration, logging and Amazon API Gateway already configured. The startup class
    /// needs to be configured in this method using the UseStartup<>() method.
    /// </summary>
    /// <param name="builder">The IWebHostBuilder to configure.</param>
    protected override void Init(IWebHostBuilder builder)
    {
        builder
            .ConfigureServices((ctx, services) =>
            {
                services
                    .AddApis(ctx.Configuration)
                    .AddApplication(ctx.Configuration)
                    .AddInfrastructure(ctx.Configuration);
            })
            .Configure(app =>
            {
                _tracerProvider = app.ApplicationServices.GetRequiredService<TracerProvider>();
                app.UsePathBase("/puckdrop");
                app.UseApis();
            });
    }

    /// <summary>
    /// Use this override to customize the services registered with the IHostBuilder. 
    /// 
    /// It is recommended not to call ConfigureWebHostDefaults to configure the IWebHostBuilder inside this method.
    /// Instead customize the IWebHostBuilder in the Init(IWebHostBuilder) overload.
    /// </summary>
    /// <param name="builder">The IHostBuilder to configure.</param>
    protected override void Init(IHostBuilder builder)
    {
    }

    public override async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandlerAsync(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext lambdaContext)
    {
        if (_tracerProvider is null)
            return await base.FunctionHandlerAsync(request, lambdaContext);

        return await AWSLambdaWrapper.TraceAsync(
            _tracerProvider, base.FunctionHandlerAsync, request, lambdaContext);
    }
}
