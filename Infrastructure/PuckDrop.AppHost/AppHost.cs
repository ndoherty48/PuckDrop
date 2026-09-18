using Aspire.Hosting.AWS.Deployment;
using Aspire.Hosting.AWS.Deployment.CDKPublishTargets;
using Aspire.Hosting.AWS.DynamoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PuckDrop.AppHost.AWS;
using PuckDrop.AppHost.AWS.Deployment;
using PuckDrop.AppHost.Extensions;
#pragma warning disable ASPIREAWSPUBLISHERS001 
#pragma warning disable ASPIREBROWSERLOGS001

var builder = DistributedApplication.CreateBuilder(args);

// Aspire keeps polling health checks for as long as the AppHost runs. This makes a check do the
// real work only until it first passes, so it stops loading the single-threaded Lambda emulator.
static Func<CancellationToken, Task<HealthCheckResult>> CheckOnceThenLatchHealthy(
    Func<CancellationToken, Task<HealthCheckResult>> check)
{
    var succeededOnce = false;
    return async cancellationToken =>
    {
        if (succeededOnce)
            return HealthCheckResult.Healthy();

        var result = await check(cancellationToken);
        if (result.Status == HealthStatus.Healthy)
            succeededOnce = true;
        return result;
    };
}

var deployedCdk = builder.AddAWSCDKEnvironment(
    "puckdrop-cdk",
    CDKDefaultsProviderFactory.Preview_V1,
    stackFactory: (app, props) => new DeploymentStack(app, "PuckDrop", props));

// Publishes the Blazor UI to S3 + CloudFront; registered the same way as the built-in AWS targets.
builder.Services.AddTransient<IAWSPublishTarget, BlazorStaticSitePublishTarget>();

var dynamoDbLocal = builder.AddAWSDynamoDBLocal("dynamodb", new DynamoDBLocalOptions
{
    SharedDb = true
});

// DynamoDB Local has no built-in health check and can report Running before it accepts
// connections, which made create-table fail. Any HTTP response (even a 400) means it's listening.
const string dynamoDbListeningCheckKey = "dynamodb-local-listening";
builder.Services.AddHealthChecks().AddAsyncCheck(dynamoDbListeningCheckKey, CheckOnceThenLatchHealthy(async cancellationToken =>
{
    try
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(dynamoDbLocal.GetEndpoint("http").Url, cancellationToken);
        return HealthCheckResult.Healthy();
    }
    catch
    {
        return HealthCheckResult.Unhealthy();
    }
}));
dynamoDbLocal.WithHealthCheck(dynamoDbListeningCheckKey);

var keycloakUsername = builder.AddParameter("keycloak-username", value: "keycloak").ExcludeFromManifest();
var keycloakPassword = builder.AddParameter("keycloak-password", secret: true, value: new GenerateParameterDefault
    {
        MinUpper = 1,
        MinLength = 8,
        MinNumeric = 1,
        MinSpecial = 1
    }, persist: true)
    .ExcludeFromManifest();

var keycloak = builder
    .AddKeycloak("keycloak", adminUsername: keycloakUsername, adminPassword: keycloakPassword)
    .WithRealmImport("./Keycloak/PuckDrop-realm.json")
    .WithExternalHttpEndpoints()
    .ExcludeFromManifest();

// Create the PuckDrop table in DynamoDB Local once it's actually accepting connections -
// WaitFor blocks on the health check registered above, not merely "Running".
var createTable = builder.AddExecutable("create-table", "aws", ".",
        builder.GetDynamoDbResourceParams(dynamoDbLocal.GetEndpoint("http")))
    .WithAWSLocalCredentials()
    .WithParentRelationship(dynamoDbLocal)
    .WaitFor(dynamoDbLocal);

var api = builder.AddAWSLambdaFunction<Projects.PuckDrop_Api>("api", "PuckDrop.Api::PuckDrop.Api.LambdaEntryPoint::FunctionHandlerAsync")
    .WithReference(dynamoDbLocal)
    .WaitFor(keycloak)
    .WithEnvironment(x =>
    {
        if(x.ExecutionContext.IsRunMode is false)
            return;

        x.EnvironmentVariables["Keycloak__ServerUrl"] = keycloak.GetEndpoint("http");
        x.EnvironmentVariables["Keycloak__Realm"] = "PuckDrop";
        x.EnvironmentVariables["Keycloak__ClientId"] = "PuckDrop-API";
        // The browser logs in with a separate client - see KeycloakSettings.UiClientId.
        x.EnvironmentVariables["Keycloak__UiClientId"] = "PuckDrop-UI";
    })
    .WithAWSLocalCredentials()
    .PublishAsLambdaFunction(new PublishLambdaFunctionConfig
    {
        PropsFunctionCallback = (_, props) =>
        {
            props.MemorySize = 1024;
        },
        ConstructFunctionCallback = (ctx, construct) =>
        {
            var stack = ctx.GetDeploymentStack<DeploymentStack>();

            // Cognito configuration
            construct
                .AddEnvironment("Cognito__UserPoolId", stack.UserPool.UserPoolId)
                .AddEnvironment("Cognito__ClientId", stack.UserPoolClient.UserPoolClientId)
                .AddEnvironment("Cognito__Region", stack.Region)
                .AddEnvironment("Cognito__AdminGroupName", "admin");

            // Grant DynamoDB read/write access
            stack.PuckDropTable.GrantReadWriteData(construct);

            // Wire Lambda to API Gateway with JWT authorizer
            stack.AddLambdaRoute(construct);
        }
    })
    .WaitForCompletion(createTable);

var apiGateway = builder.AddAWSAPIGatewayEmulator("api-gateway", Aspire.Hosting.AWS.Lambda.APIGatewayType.HttpV2)
    .WithReference(api, Aspire.Hosting.AWS.Lambda.Method.Any, "/puckdrop/{proxy+}")
    .WithHttpEndpoint(port: 8080)
    .WithHttpsEndpoint(port: 8081);

// The API Gateway emulator also has no health check and can 404 before the Lambda route is live.
// /puckdrop/auth-config is cheap and unauthenticated, so a 200 proves the whole path works.
const string apiGatewayReadyCheckKey = "api-gateway-auth-config-ready";
builder.Services.AddHealthChecks().AddAsyncCheck(apiGatewayReadyCheckKey, CheckOnceThenLatchHealthy(async cancellationToken =>
{
    try
    {
        using var client = new HttpClient();
        // HTTP, not HTTPS: the TLS handshake against the self-signed dev cert hangs.
        var authConfigUrl = new Uri(new Uri(apiGateway.GetEndpoint("http").Url), "/puckdrop/auth-config");
        using var response = await client.GetAsync(authConfigUrl, cancellationToken);
        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Got HTTP {(int)response.StatusCode}");
    }
    catch (Exception ex)
    {
        return HealthCheckResult.Unhealthy(exception: ex);
    }
}));
apiGateway.WithHealthCheck(apiGatewayReadyCheckKey);

var web = builder.AddBlazorWasmProject<Projects.PuckDrop_Web>("web")
    .WithReference(apiGateway.GetEndpoint("http"))
    .PublishAsS3WithCloudFront(config =>
    {
        // Europe only: EU, EEA, UK, Switzerland, Crown Dependencies and Gibraltar. Covers
        // /puckdrop/* too, but not the direct API Gateway URL or Cognito's login domain.
        config.PropsDistributionCallback = (_, props) =>
        {
            props.GeoRestriction = Amazon.CDK.AWS.CloudFront.GeoRestriction.Allowlist(
                "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE",
                "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
                "IS", "LI", "NO",
                "GB", "CH",
                "JE", "GG", "IM", "GI");
        };
    });

var blazorGateway = builder.AddBlazorGateway("blazor-gateway")
    .WithExternalHttpEndpoints();

// WithOtlpExporter and WithBrowserLogs need the dashboard, which the E2E tests don't run.
var hasDashboard = !string.IsNullOrEmpty(builder.Configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"]);
if (hasDashboard)
    blazorGateway.WithOtlpExporter(OtlpProtocol.HttpProtobuf);

// Needed in publish mode too: it sets web's Parent, and publishing throws if that's null.
blazorGateway.WithBlazorClientApp(web);

if (hasDashboard)
    blazorGateway.WithBrowserLogs();

builder.Build().Run();
