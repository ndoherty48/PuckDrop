using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace PuckDrop.AppHost.AWS;

public class DeploymentStack : Stack
{
    public Table PuckDropTable { get; private set; } = null!;
    public UserPool UserPool { get; private set; } = null!;
    public UserPoolClient UserPoolClient { get; private set; } = null!;
    public CfnApi HttpApi { get; private set; } = null!;
    public CfnAuthorizer JwtAuthorizer { get; private set; } = null!;

    public DeploymentStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
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
            // RETAIN above protects the table from being deleted along with the stack, but does
            // nothing against a bad admin action or app bug corrupting/wiping real data (e.g. a
            // season's UserAnswer items). PITR is cheap at this table's size and gives a 35-day
            // restore-to-any-point safety net for exactly that case.
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
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
                RequireDigits = true,
                RequireSymbols = false
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = RemovalPolicy.RETAIN,
            // Optional, not required - a friend-group app shouldn't force every user through
            // MFA, but the "admin" group can score polls, which the whole season leaderboard's
            // integrity rests on, so admins need the option. TOTP only (no SMS - avoids per-use
            // SMS cost for a feature most users won't turn on).
            Mfa = Mfa.OPTIONAL,
            MfaSecondFactor = new MfaSecondFactor
            {
                Otp = true,
                Sms = false
            }
        });

        UserPoolClient = UserPool.AddClient("PuckDropWebClient", new UserPoolClientOptions
        {
            UserPoolClientName = "PuckDrop-Web",
            // No direct AuthFlows (USER_SRP_AUTH etc.) - the Blazor client only ever uses the
            // OAuth Authorization Code flow via Cognito's Hosted UI (see AuthDiscoveryOptions'
            // hardcoded ResponseType="code"), never Cognito's InitiateAuth API directly.
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

        // Cognito hosted-domain prefixes are unique across every AWS account in the partition,
        // not just this one - a bare "puckdrop" risks colliding with someone else's pool and
        // failing at deploy time with no way to know in advance. Suffixing with the account ID
        // makes it unique to this deployment instead.
        UserPool.AddDomain("PuckDropDomain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions { DomainPrefix = $"puckdrop-{Account}" }
            // ManagedLoginVersion left at its default (NEWER_MANAGED_LOGIN) - see the
            // CfnManagedLoginBranding below for why that needs its own resource.
        });

        // An app client created via CloudFormation/the SDK (as this one is) gets no managed-login
        // branding style at all - AWS's own docs confirm managed login "isn't available for an
        // app client created with an AWS SDK until you create one with a
        // CreateManagedLoginBranding request" - and without one, the hosted login page shows
        // "Login pages unavailable. Please contact an administrator." (confirmed live, via a real
        // deployed distribution). UseCognitoProvidedValues = true satisfies that requirement with
        // Cognito's own default look - no custom logo/colors needed for this app today, but
        // Managed Login (vs. the older Classic Hosted UI) leaves room to add real branding, or
        // pick up features like passkey sign-in, later without changing the domain's branding
        // version again.
        _ = new CfnManagedLoginBranding(this, "PuckDropManagedLoginBranding", new CfnManagedLoginBrandingProps
        {
            UserPoolId = UserPool.UserPoolId,
            ClientId = UserPoolClient.UserPoolClientId,
            UseCognitoProvidedValues = true
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

        // Route: GET /puckdrop/auth-config, deliberately unauthenticated - the Blazor WASM app
        // fetches its OIDC config from here at boot (see AuthConfigController), so it can't
        // itself require a token yet. HTTP APIs match the most specific route over the
        // {proxy+} catch-all above, so this exact-path route safely coexists with the blanket
        // JWT authorizer on every other path under /puckdrop/ without opening anything else up.
        _ = new Amazon.CDK.AWS.Apigatewayv2.CfnRoute(this, "PuckDropAuthConfigRoute", new Amazon.CDK.AWS.Apigatewayv2.CfnRouteProps
        {
            ApiId = HttpApi.Ref,
            RouteKey = "GET /puckdrop/auth-config",
            Target = $"integrations/{integration.Ref}",
            AuthorizationType = "NONE"
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
        // Plain string interpolation, not Fn.Sub - CDK's own token-resolution machinery already
        // encodes HttpApi.Ref correctly here (same proven pattern as JwtAuthorizer's Issuer
        // above and BlazorStaticSitePublishTarget's CloudFront origin). Wrapping it in Fn.Sub's
        // own "${...}" template syntax on top of that double-encodes the token into nested
        // "${${...}}" braces, which is what a real `aspire deploy` attempt actually hit:
        // "One or more Fn::Sub intrinsic functions don't specify expected arguments" - confirmed
        // via CloudFormation's own template validation warning naming this exact output.
        _ = new CfnOutput(this, "ApiGatewayUrl", new CfnOutputProps
        {
            Value = $"https://{HttpApi.Ref}.execute-api.{Region}.amazonaws.com"
        });
    }
}
