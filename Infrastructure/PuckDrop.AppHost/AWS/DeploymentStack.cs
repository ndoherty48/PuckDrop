using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Aspire.Hosting.AWS.Deployment;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace PuckDrop.AppHost.AWS;

public class DeploymentStack : Stack
{
    [DefaultVpc]
    public IVpc DefaultVpc { get; private set; } = null!;

    [DefaultECSCluster]
    public ICluster DefaultECSCluster { get; private set; } = null!;

    public Table PuckDropTable { get; private set; } = null!;
    public UserPool UserPool { get; private set; } = null!;
    public UserPoolClient UserPoolClient { get; private set; } = null!;
    public CfnApi HttpApi { get; private set; } = null!;
    public CfnAuthorizer JwtAuthorizer { get; private set; } = null!;

    public DeploymentStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
        DefaultVpc = Vpc.FromLookup(this, "DefaultVpc", new VpcLookupOptions { IsDefault = true });

        DefaultECSCluster = new Cluster(this, "MyCluster", new ClusterProps
        {
            Vpc = DefaultVpc,
            ClusterName = "my-aspire-cluster"
        });

        CreateDynamoDbTable();
        CreateCognitoResources();
        CreateApiGateway();
        CreateOutputs();
    }

    private void CreateDynamoDbTable()
    {
        PuckDropTable = new Table(this, "PuckDropTable", new TableProps
        {
            TableName = "PuckDrop",
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.RETAIN,
            PartitionKey = new Attribute { Name = "PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "SK", Type = AttributeType.STRING }
        });

        AddGsi("GSI1", "GSI1PK", "GSI1SK");
        AddGsi("GSI2", "GSI2PK", "GSI2SK");
    }

    private void AddGsi(string indexName, string partitionKey, string sortKey)
    {
        PuckDropTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = indexName,
            PartitionKey = new Attribute { Name = partitionKey, Type = AttributeType.STRING },
            SortKey = new Attribute { Name = sortKey, Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });
    }

    private void CreateCognitoResources()
    {
        UserPool = new UserPool(this, "PuckDropUserPool", new UserPoolProps
        {
            UserPoolName = "PuckDrop",
            SelfSignUpEnabled = false,
            SignInAliases = new SignInAliases { Email = true },
            AutoVerify = new AutoVerifiedAttrs { Email = true },
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute { Required = true },
                PreferredUsername = new StandardAttribute { Required = true }
            },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 8,
                RequireUppercase = false,
                RequireDigits = false,
                RequireSymbols = false
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        UserPoolClient = UserPool.AddClient("PuckDropWebClient", new UserPoolClientOptions
        {
            UserPoolClientName = "PuckDrop-Web",
            AuthFlows = new AuthFlow { UserSrp = true },
            OAuth = new OAuthSettings
            {
                Flows = new OAuthFlows { AuthorizationCodeGrant = true },
                Scopes = [OAuthScope.OPENID, OAuthScope.EMAIL, OAuthScope.PROFILE],
                CallbackUrls = ["https://localhost/authentication/login-callback"],
                LogoutUrls = ["https://localhost/"]
            },
            PreventUserExistenceErrors = true
        });

        _ = new CfnUserPoolGroup(this, "AdminGroup", new CfnUserPoolGroupProps
        {
            UserPoolId = UserPool.UserPoolId,
            GroupName = "admin",
            Description = "Administrators who can create/score polls"
        });

        UserPool.AddDomain("PuckDropDomain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions { DomainPrefix = "puckdrop" }
        });
    }

    private void CreateApiGateway()
    {
        // HTTP API v2
        HttpApi = new CfnApi(this, "PuckDropApi", new CfnApiProps
        {
            Name = "PuckDrop",
            ProtocolType = "HTTP",
            CorsConfiguration = new CfnApi.CorsProperty
            {
                AllowOrigins = ["*"],
                AllowMethods = ["GET", "POST", "PUT", "DELETE", "OPTIONS"],
                AllowHeaders = ["Authorization", "Content-Type"]
            }
        });

        // JWT Authorizer backed by Cognito
        JwtAuthorizer = new CfnAuthorizer(this, "PuckDropJwtAuthorizer", new CfnAuthorizerProps
        {
            ApiId = HttpApi.Ref,
            AuthorizerType = "JWT",
            Name = "CognitoJwtAuthorizer",
            IdentitySource = ["$request.header.Authorization"],
            JwtConfiguration = new CfnAuthorizer.JWTConfigurationProperty
            {
                Issuer = $"https://cognito-idp.{Region}.amazonaws.com/{UserPool.UserPoolId}",
                Audience = [UserPoolClient.UserPoolClientId]
            }
        });

        // Auto-deploy stage
        var stage = new CfnStage(this, "PuckDropApiStage", new CfnStageProps
        {
            ApiId = HttpApi.Ref,
            StageName = "$default",
            AutoDeploy = true
        });
    }

    /// <summary>
    /// Adds a Lambda integration and route to the HTTP API.
    /// Called from the ConstructFunctionCallback in the AppHost.
    /// </summary>
    public void AddLambdaRoute(Amazon.CDK.AWS.Lambda.Function lambdaFunction)
    {
        // Lambda integration
        var integration = new CfnIntegration(this, "PuckDropLambdaIntegration", new CfnIntegrationProps
        {
            ApiId = HttpApi.Ref,
            IntegrationType = "AWS_PROXY",
            IntegrationUri = lambdaFunction.FunctionArn,
            PayloadFormatVersion = "2.0"
        });

        // Route: ANY /puckdrop/{proxy+} with JWT authorizer
        _ = new Amazon.CDK.AWS.Apigatewayv2.CfnRoute(this, "PuckDropApiRoute", new Amazon.CDK.AWS.Apigatewayv2.CfnRouteProps
        {
            ApiId = HttpApi.Ref,
            RouteKey = "ANY /puckdrop/{proxy+}",
            Target = $"integrations/{integration.Ref}",
            AuthorizationType = "JWT",
            AuthorizerId = JwtAuthorizer.Ref
        });

        // Grant API Gateway permission to invoke the Lambda
        lambdaFunction.AddPermission("ApiGatewayInvoke", new Amazon.CDK.AWS.Lambda.Permission
        {
            Principal = new Amazon.CDK.AWS.IAM.ServicePrincipal("apigateway.amazonaws.com"),
            SourceArn = $"arn:aws:execute-api:{Region}:{Account}:{HttpApi.Ref}/*"
        });
    }

    private void CreateOutputs()
    {
        _ = new CfnOutput(this, "UserPoolId", new CfnOutputProps { Value = UserPool.UserPoolId });
        _ = new CfnOutput(this, "UserPoolClientId", new CfnOutputProps { Value = UserPoolClient.UserPoolClientId });
        _ = new CfnOutput(this, "DynamoDbTableName", new CfnOutputProps { Value = PuckDropTable.TableName });
        _ = new CfnOutput(this, "ApiGatewayUrl", new CfnOutputProps
        {
            Value = Fn.Sub($"https://${{{HttpApi.Ref}}}.execute-api.{Region}.amazonaws.com")
        });
    }
}
