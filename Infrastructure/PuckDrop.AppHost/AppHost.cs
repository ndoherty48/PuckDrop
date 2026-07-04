using Amazon;
using Aspire.Hosting.AWS.Deployment;
#pragma warning disable ASPIREAWSPUBLISHERS001 
#pragma warning disable ASPIREBROWSERLOGS001

var builder = DistributedApplication.CreateBuilder(args);

var awsSdk = builder.AddAWSSDKConfig()
    .WithRegion(RegionEndpoint.EUWest2)
    .WithProfile("PuckDrop-Dublin");

builder.AddAWSCDKEnvironment("puckdrop-cdk", CDKDefaultsProviderFactory.Preview_V1);

var dynamoDbLocal = builder.AddAWSDynamoDBLocal("dynamodb");
var api = builder.AddAWSLambdaFunction<Projects.PuckDrop_Api>("api", "PuckDrop.Api::PuckDrop.Api.LambdaEntryPoint::FunctionHandlerAsync")
    .WithReference(dynamoDbLocal);
var apiGateway = builder.AddAWSAPIGatewayEmulator("api-gateway", Aspire.Hosting.AWS.Lambda.APIGatewayType.HttpV2)
    .WithReference(api, Aspire.Hosting.AWS.Lambda.Method.Any, "/puckdrop/{proxy+}")
    .WithHttpEndpoint(port: 8080)
    .WithHttpsEndpoint(port: 8081);

var web = builder.AddBlazorWasmProject<Projects.PuckDrop_Web>("web")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"));

builder.AddBlazorGateway("blazor-gateway")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithBlazorClientApp(web)
    .WithBrowserLogs();

builder.Build().Run();
