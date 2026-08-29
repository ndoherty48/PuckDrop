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
// Pinned (not dynamically allocated): the Blazor WASM app can't read AppHost-injected env
// vars at runtime (see Program.cs), so its Keycloak authority is baked into
// UI/src/PuckDrop.Web/wwwroot/appsettings.json at build time. A stable port here means that
// file only needs to be set once instead of updated by hand on every `aspire start`.
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

// Blazor WASM can't read AppHost-injected env vars at runtime (see Program.cs), so there's no
// .WithEnvironment(...) here wiring up the "Oidc" OIDC settings - they're baked into
// UI/src/PuckDrop.Web/wwwroot/appsettings.json at build time instead, which is why Keycloak's
// port is pinned above rather than dynamically allocated.
var web = builder.AddBlazorWasmProject<Projects.PuckDrop_Web>("web")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"));

builder.AddBlazorGateway("blazor-gateway")
    .WithEnvironment("ApiClientSettings__BaseUrl", apiGateway.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithBlazorClientApp(web)
    .WithBrowserLogs();

builder.Build().Run();
