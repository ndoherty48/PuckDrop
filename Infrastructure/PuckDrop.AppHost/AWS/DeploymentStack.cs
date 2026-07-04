using Amazon.CDK;
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
    public DeploymentStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
        // Use the account's default VPC instead of creating a new one
        DefaultVpc = Vpc.FromLookup(this, "DefaultVpc", new VpcLookupOptions
        {
            IsDefault = true
        });
        
        // Create a custom ECS cluster with specific configuration
        DefaultECSCluster = new Cluster(this, "MyCluster", new ClusterProps
        {
            Vpc = DefaultVpc,
            ClusterName = "my-aspire-cluster"
        });

        // ─── DynamoDB ─────────────────────────────────────────────────────────

        PuckDropTable = new Table(this, "PuckDropTable", new TableProps
        {
            TableName = "PuckDrop",
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.RETAIN,
            PartitionKey = new Attribute { Name = "PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "SK", Type = AttributeType.STRING }
        });

        // GSI1 — Poll-centric queries (poll + questions + options, all answers for a poll)
        PuckDropTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "GSI1",
            PartitionKey = new Attribute { Name = "GSI1PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "GSI1SK", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        // GSI2 — Active poll lookup by season + status
        PuckDropTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "GSI2",
            PartitionKey = new Attribute { Name = "GSI2PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "GSI2SK", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        // ─── Cognito ──────────────────────────────────────────────────────────

        // User Pool
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

        // App client for the Blazor WASM frontend
        UserPoolClient = UserPool.AddClient("PuckDropWebClient", new UserPoolClientOptions
        {
            UserPoolClientName = "PuckDrop-Web",
            AuthFlows = new AuthFlow
            {
                UserSrp = true
            },
            OAuth = new OAuthSettings
            {
                Flows = new OAuthFlows { AuthorizationCodeGrant = true },
                Scopes = [OAuthScope.OPENID, OAuthScope.EMAIL, OAuthScope.PROFILE],
                CallbackUrls = ["https://localhost/authentication/login-callback"],
                LogoutUrls = ["https://localhost/"]
            },
            PreventUserExistenceErrors = true
        });

        // Admin group — matches the cognito:groups claim checked by the API
        _ = new CfnUserPoolGroup(this, "AdminGroup", new CfnUserPoolGroupProps
        {
            UserPoolId = UserPool.UserPoolId,
            GroupName = "admin",
            Description = "Administrators who can create/score polls"
        });

        // Hosted UI domain (Cognito's built-in login page)
        UserPool.AddDomain("PuckDropDomain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions
            {
                DomainPrefix = "puckdrop"
            }
        });

        // ─── Outputs ──────────────────────────────────────────────────────────

        _ = new CfnOutput(this, "UserPoolId", new CfnOutputProps
        {
            Value = UserPool.UserPoolId,
            Description = "Cognito User Pool ID for API appsettings"
        });

        _ = new CfnOutput(this, "UserPoolClientId", new CfnOutputProps
        {
            Value = UserPoolClient.UserPoolClientId,
            Description = "Cognito App Client ID for API appsettings"
        });

        _ = new CfnOutput(this, "DynamoDbTableName", new CfnOutputProps
        {
            Value = PuckDropTable.TableName,
            Description = "DynamoDB table name"
        });
    }

    [DefaultVpc]
    public IVpc DefaultVpc { get; private set; }
    
    [DefaultECSCluster]
    public ICluster DefaultECSCluster { get; private set; }

    public Table PuckDropTable { get; private set; }
    public UserPool UserPool { get; private set; }
    public UserPoolClient UserPoolClient { get; private set; }
}
