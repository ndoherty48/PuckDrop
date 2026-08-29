using Aspire.Hosting.AWS.Deployment;
using Aspire.Hosting.AWS.DynamoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PuckDrop.AppHost.AWS;
using PuckDrop.AppHost.Extensions;
#pragma warning disable ASPIREAWSPUBLISHERS001 
#pragma warning disable ASPIREBROWSERLOGS001

var builder = DistributedApplication.CreateBuilder(args);

var deployedCdk = builder.AddAWSCDKEnvironment(
    "puckdrop-cdk",
    CDKDefaultsProviderFactory.Preview_V1,
    stackFactory: (app, props) => new DeploymentStack(app, "PuckDrop", props));

var dynamoDbLocal = builder.AddAWSDynamoDBLocal("dynamodb", new DynamoDBLocalOptions
{
    SharedDb = true
});

// AddAWSDynamoDBLocal registers no health check of its own (confirmed by inspecting
// Aspire.Hosting.AWS.dll directly - it has zero health-check-related members), so without this,
// create-table's WaitFor(dynamoDbLocal) below only ever waits for the container to reach
// "Running" - not for its HTTP listener to actually accept connections. DynamoDB Local can
// report Running slightly before that's true, so create-table would sometimes fire too early and
// fail ("Connection was closed before we received a valid response"), cascading into "api" never
// starting. Any completed HTTP response (even the 400 DynamoDB Local returns for an unsigned
// request) proves the listener is up; only a connection failure means it isn't ready yet.
const string dynamoDbListeningCheckKey = "dynamodb-local-listening";
builder.Services.AddHealthChecks().AddAsyncCheck(dynamoDbListeningCheckKey, async cancellationToken =>
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
});
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
// Historically pinned (not dynamically allocated) because the Blazor WASM app couldn't read
// AppHost-injected env vars, so its Keycloak authority had to be baked into
// UI/src/PuckDrop.Web/wwwroot/appsettings.json by hand at build time - a stable port meant
// that file only needed setting once. That's no longer why this matters: the UI now fetches its
// OIDC config at runtime from the API's /auth-config endpoint (see PuckDrop.Api.Controllers.
// AuthConfigController), which gets Keycloak's real endpoint dynamically via the `api` resource's
// own env vars below - real env vars work fine for the Lambda process, unlike the WASM bundle.
// Left pinned anyway; there's no reason to change it, just no longer a requirement.
var keycloak = builder
    .AddKeycloak("keycloak", port: 8543, adminUsername: keycloakUsername, adminPassword: keycloakPassword)
    .WithRealmImport("./Keycloak/PuckDrop-realm.json")
    // Keycloak__ServerUrl below ends up as the OIDC Authority served to the browser (via
    // /auth-config -> AuthDiscoveryOptions), not just consumed server-side by "api" - so
    // GetEndpoint("http") needs to resolve to a URL the browser can actually reach. Without this,
    // it resolved to the "internal" plain-localhost form instead, which Keycloak's own hostname
    // handling would intermittently refuse (net::ERR_CONNECTION_CLOSED - confirmed by
    // reproducing it directly in a browser). Matches the same call already made on blazorGateway
    // below for the same reason.
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
        // The browser's login client, distinct from the API's own audience-validation client
        // above - see KeycloakSettings.UiClientId.
        x.EnvironmentVariables["Keycloak__UiClientId"] = "PuckDrop-UI";
    })
    .WithAWSLocalCredentials()
    .PublishAsLambdaFunction(new PublishLambdaFunctionConfig
    {
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
    .WithHttpsEndpoint(port: 8081)
    // Same gap as dynamodb above: AddAWSAPIGatewayEmulator registers no health check of its own
    // (confirmed the same way - zero health-check members anywhere in Aspire.Hosting.AWS.dll), so
    // this resource can report "Running" before its route table (which depends on the "api"
    // Lambda function being fully wired as a target) is actually serving requests - callers can
    // get a spurious 404 in that window. /puckdrop/auth-config is a real, cheap, unauthenticated
    // route that only returns 200 once API Gateway -> Lambda -> ASP.NET Core routing is genuinely
    // working end to end, so it doubles as a true readiness probe, not just "is the port open".
    // Pinned to the "http" endpoint explicitly - left to its default, this picked the "https"
    // endpoint instead and hung indefinitely completing a TLS handshake against the self-signed
    // local-dev cert (confirmed live: the health check's own HttpClient never got past
    // EnsureFullTlsFrameAsync). Plain HTTP has no such problem and is just as valid a readiness
    // signal for local dev.
    .WithHttpHealthCheck(path: "/puckdrop/auth-config", endpointName: "http");

// Blazor WASM still can't read AppHost-injected env vars at runtime (see Program.cs), but that
// no longer matters for OIDC config specifically - the app fetches it at boot from the API's
// /auth-config endpoint instead, so there's nothing to wire up here for it.
var web = builder.AddBlazorWasmProject<Projects.PuckDrop_Web>("web")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"));

var blazorGateway = builder.AddBlazorGateway("blazor-gateway")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

// Both .WithOtlpExporter and .WithBrowserLogs (dashboard dev-tooling: tracks a browser tab and
// reports its console diagnostics back via OTLP) require a real Aspire Dashboard supplying
// ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL (set by `aspire start`/`aspire run`) - absent when
// running under Aspire.Hosting.Testing (tests/PuckDrop.E2ETests boots the AppHost with no
// dashboard, and drives its own separate Playwright browser - Aspire's tracked-browser-tab
// tooling isn't wanted there anyway), where both fail resource startup hard instead of just
// skipping. Guard both rather than break that test suite.
var hasDashboard = !string.IsNullOrEmpty(builder.Configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"]);
if (hasDashboard)
    blazorGateway.WithOtlpExporter(OtlpProtocol.HttpProtobuf);

blazorGateway.WithBlazorClientApp(web);

if (hasDashboard)
    blazorGateway.WithBrowserLogs();

builder.Build().Run();
