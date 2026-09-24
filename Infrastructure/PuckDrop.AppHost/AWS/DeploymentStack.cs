using System.Text.Json;
using System.Text.Json.Nodes;
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
        // V2 is a replacement pool, not an edit of the original. Case sensitivity, the sign-in
        // attributes and which standard attributes are required are all immutable once a pool
        // exists - CloudFormation reports UsernameConfiguration as "no interruption" and
        // UpdateUserPool has no parameter for it, so an in-place change deploys green and does
        // nothing. The V1 pool keeps RemovalPolicy.RETAIN: it's orphaned rather than deleted, so
        // it stays readable while users and their answer history move across.
        UserPool = new UserPool(this, "PuckDropUserPoolV2", new UserPoolProps
        {
            UserPoolName = "PuckDrop",
            SelfSignUpEnabled = false,
            // Email is the sign-in identifier (CDK emits UsernameAttributes, not AliasAttributes);
            // the username underneath is a UUID Cognito generates.
            SignInAliases = new SignInAliases { Email = true },
            // The reason for V2. Left at its default of true, Nathan@example.com and
            // nathan@example.com are separate accounts, and managed login reports the wrong
            // casing as a bad password (PreventUserExistenceErrors hides the real cause).
            SignInCaseSensitive = false,
            AutoVerify = new AutoVerifiedAttrs { Email = true },
            // Mutability is itself immutable, so say it rather than inherit it: preferred_username
            // is the leaderboard display name and people need to be able to change it.
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute { Required = true, Mutable = true },
                PreferredUsername = new StandardAttribute { Required = true, Mutable = true }
            },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 8,
                RequireUppercase = false,
                RequireDigits = true,
                RequireSymbols = false,
                // Cognito defaults to 7 days, which assumes people open their email promptly.
                TempPasswordValidity = Duration.Days(30)
            },
            // Cognito requires {username} and {####} in an invitation template. No sign-in URL:
            // the CloudFront domain doesn't exist yet at pool-creation time.
            UserInvitation = new UserInvitationConfig
            {
                EmailSubject = "You're in: PuckDrop",
                EmailBody = "Your PuckDrop account is ready.<br/><br/>" +
                    "Email: {username}<br/>Temporary password: {####}<br/><br/>" +
                    "Sign in before the next game day and get your picks in."
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = RemovalPolicy.RETAIN,
            // RemovalPolicy only guards the CloudFormation path; this guards the console and API.
            DeletionProtection = true,
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
            OAuth = new OAuthSettings
            {
                Flows = new OAuthFlows { AuthorizationCodeGrant = true },
                Scopes = [OAuthScope.OPENID, OAuthScope.EMAIL, OAuthScope.PROFILE],
                CallbackUrls = ["https://localhost/authentication/login-callback"],
                LogoutUrls = ["https://localhost/"]
            },
            PreventUserExistenceErrors = true,
            RefreshTokenValidity = Duration.Days(30)
        });

        // Rotation limits how long a refresh token copied from localStorage stays usable. It's only
        // on the L1 resource, hence the escape hatch. The grace period covers two tabs refreshing
        // at once.
        if (UserPoolClient.Node.DefaultChild is not CfnUserPoolClient cfnUserPoolClient)
            throw new InvalidOperationException(
                "Expected UserPoolClient's default child to be a CfnUserPoolClient - " +
                "Amazon.CDK.Lib's Cognito construct shape may have changed.");

        cfnUserPoolClient.RefreshTokenRotation = new CfnUserPoolClient.RefreshTokenRotationProperty
        {
            Feature = "ENABLED",
            RetryGracePeriodSeconds = 60
        };

        _ = new CfnUserPoolGroup(this, "AdminGroup", new CfnUserPoolGroupProps
        {
            UserPoolId = UserPool.UserPoolId,
            GroupName = "admin",
            Description = "Administrators who can create/score polls"
        });

        // Domain prefixes are global across all AWS accounts, so add the account ID. The "v2" is
        // because the retained V1 pool still owns the original prefix and CloudFormation creates
        // this domain before deleting that one - they can't share a name for the overlap. Drop the
        // suffix in a later deploy once V1 is gone; the OIDC authority is the issuer URL, not this
        // domain, so the UI picks the change up from discovery without a rebuild.
        UserPool.AddDomain("PuckDropDomain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions { DomainPrefix = $"puckdrop-v2-{Account}" },
            // Branding styles only apply to managed login. The CDK default leaves a domain on the
            // classic hosted UI, which ignores them entirely.
            ManagedLoginVersion = ManagedLoginVersion.NEWER_MANAGED_LOGIN
        });

        CreateManagedLoginBranding();
    }

    /// <summary>
    /// Managed login branding, in two stages. Cognito publishes no schema for the settings
    /// document and silently drops keys it doesn't recognise, so the only reliable source is a
    /// describe against a live style:
    /// <code>
    /// aws cognito-idp describe-managed-login-branding-by-client     ///   --user-pool-id &lt;pool&gt; --client-id &lt;client&gt; --return-merged-resources     ///   --query 'ManagedLoginBranding.Settings' &gt; AWS/Branding/branding-settings.json
    /// </code>
    /// Until that file is committed, deploy Cognito's own defaults - a client created through
    /// CloudFormation with no style at all shows "Login pages unavailable". Settings and
    /// UseCognitoProvidedValues are mutually exclusive, and so are the assets, so the logos only
    /// go up once there's a settings document to hang them on.
    /// </summary>
    private void CreateManagedLoginBranding()
    {
        var settings = LoadBrandingSettings();

        _ = new CfnManagedLoginBranding(this, "PuckDropManagedLoginBranding", new CfnManagedLoginBrandingProps
        {
            UserPoolId = UserPool.UserPoolId,
            ClientId = UserPoolClient.UserPoolClientId,
            UseCognitoProvidedValues = settings is null,
            Settings = settings,
            Assets = settings is null ? null : BrandingAssets()
        });
    }

    /// <summary>
    /// The logos, keyed by where managed login uses them. ColorMode is the browser's light/dark
    /// preference, not the colour of the surface behind the logo - app.css has no dark theme, so
    /// everything is LIGHT except the favicon, which DYNAMIC renders in every context.
    /// </summary>
    private static object[] BrandingAssets() =>
    [
        // Sits on the white form card, so the wordmark is --pd-ink.
        BrandingAsset("FORM_LOGO", "LIGHT", "puckdrop-lockup-ink.svg"),
        // Sits on the --pd-boards header, so the wordmark is --pd-frost.
        BrandingAsset("PAGE_HEADER_LOGO", "LIGHT", "puckdrop-lockup-frost.svg"),
        BrandingAsset("FAVICON_SVG", "DYNAMIC", "puckdrop-favicon.svg")
    ];

    private static CfnManagedLoginBranding.AssetTypeProperty BrandingAsset(
        string category, string colorMode, string fileName) =>
        new()
        {
            Category = category,
            ColorMode = colorMode,
            Extension = "SVG",
            Bytes = Convert.ToBase64String(File.ReadAllBytes(BrandingPath(fileName)))
        };

    private static object? LoadBrandingSettings()
    {
        var path = BrandingPath("branding-settings.json");
        if (!File.Exists(path))
            return null;

        var node = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty.");

        return ToJsiiValue(node);
    }

    /// <summary>
    /// JSII serialises plain dictionaries, lists and primitives; it doesn't understand
    /// <see cref="JsonNode"/>, so the parsed document is walked into those types.
    /// </summary>
    private static object? ToJsiiValue(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(pair => pair.Key, pair => ToJsiiValue(pair.Value)),
        JsonArray array => array.Select(ToJsiiValue).ToArray(),
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetValue<double>(),
            _ => value.GetValue<string>()
        },
        _ => throw new InvalidOperationException($"Unexpected JSON node {node.GetType().Name}.")
    };

    private static string BrandingPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "AWS", "Branding", fileName);

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

        // Route: GET /puckdrop/auth-config without auth - the UI fetches its OIDC config here
        // before it has a token. The exact path takes precedence over {proxy+}.
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
        _ = new CfnOutput(this, "ApiGatewayUrl", new CfnOutputProps
        {
            Value = $"https://{HttpApi.Ref}.execute-api.{Region}.amazonaws.com"
        });
    }
}
