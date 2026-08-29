using Aspire.Hosting.AWS.Deployment;
using Aspire.Hosting.AWS.DynamoDB;
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
    .ExcludeFromManifest();

// Create the PuckDrop table in DynamoDB Local after it's healthy
var createTable = builder.AddExecutable("create-table", "aws", ".",
        builder.GetDynamoDbResourceParams(dynamoDbLocal.GetEndpoint("http")))
    .WithAWSLocalCredentials()
    .WithParentRelationship(dynamoDbLocal)
    .WaitFor(dynamoDbLocal);

var api = builder.AddAWSLambdaFunction<Projects.PuckDrop_Api>("api", "PuckDrop.Api::PuckDrop.Api.LambdaEntryPoint::FunctionHandlerAsync")
    .WithReference(dynamoDbLocal)
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
    .WithHttpsEndpoint(port: 8081);

// Blazor WASM still can't read AppHost-injected env vars at runtime (see Program.cs), but that
// no longer matters for OIDC config specifically - the app fetches it at boot from the API's
// /auth-config endpoint instead, so there's nothing to wire up here for it.
var web = builder.AddBlazorWasmProject<Projects.PuckDrop_Web>("web")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"));

builder.AddBlazorGateway("blazor-gateway")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithBlazorClientApp(web)
    .WithBrowserLogs();

builder.Build().Run();
