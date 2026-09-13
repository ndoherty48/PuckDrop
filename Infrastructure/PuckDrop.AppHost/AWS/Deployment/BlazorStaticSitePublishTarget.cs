#pragma warning disable ASPIREAWSPUBLISHERS001
#pragma warning disable ASPIREBLAZOR001

using System.Diagnostics;
using System.Text.RegularExpressions;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Deployment;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.AWS.Deployment;
using Aspire.Hosting.AWS.Deployment.CDKDefaults;
using Aspire.Hosting.AWS.Deployment.CDKPublishTargets;
using Microsoft.Extensions.Logging;
using IResource = Aspire.Hosting.ApplicationModel.IResource;

namespace PuckDrop.AppHost.AWS.Deployment;

/// <summary>
/// Publishes the Blazor WebAssembly UI (<see cref="BlazorWasmAppResource"/>) as an S3 bucket
/// fronted by a CloudFront distribution: static assets from S3 with a CloudFront behavior
/// routing <c>/puckdrop/*</c> to the real API Gateway, HTTPS via CloudFront, SPA-style
/// 403/404 -&gt; <c>index.html</c> fallback for client-side routing.
///
/// Adapted from AWS's own (unreleased) JavaScript-app equivalent
/// (aws/integrations-on-dotnet-aspire-for-aws#203's <c>S3StaticWebsitePublishTarget</c>) - the
/// CDK-construct-building logic (bucket + OAC, CloudFront distribution, SPA fallback, bucket
/// deployment) is resource-type-agnostic and ported closely; three things are genuinely
/// different for Blazor rather than a JavaScript app:
///  1. The build step runs `dotnet publish -c Release` instead of `npm run build`.
///  2. The `/puckdrop/*` backend behavior is baked in directly (reading the CDK-only
///     <see cref="DeploymentStack.HttpApi"/> via <see cref="DeploymentStack"/>) rather than
///     resolved generically through another Aspire-published resource - PuckDrop's API Gateway
///     isn't wrapped by one.
///  3. The Cognito user pool client's OAuth callback/logout URLs, created with a placeholder in
///     <see cref="DeploymentStack"/> (before the CloudFront domain exists), are corrected here
///     via the standard CDK "escape hatch" (<c>Node.DefaultChild</c>) once the distribution's
///     domain is known.
///
/// <see cref="Aspire.Hosting.AWS.Deployment.AWSCDKEnvironmentResource"/>'s own CDK stack
/// (<c>CDKStack</c>) is internal to <c>Aspire.Hosting.AWS</c>, so constructs here are scoped to
/// PuckDrop's own <see cref="DeploymentStack"/> (reached via
/// <see cref="AbstractAWSPublishTarget.CreatePublishTargetContext"/> /
/// <see cref="CDKPublishTargetContext.GetDeploymentStack{T}"/> - the same mechanism the existing
/// Lambda `ConstructFunctionCallback` in <c>AppHost.cs</c> already uses), which is itself the
/// CDK stack construct, so it works equally well as a construct scope.
///
/// <see cref="CDKDefaultsProvider"/> in the currently-installed Aspire.Hosting.AWS package has
/// no S3/CloudFront-specific defaults (that's PR #203-only, added there as a partial-class
/// extension inside the same assembly, which can't be replicated from outside it) - the default
/// values below are applied directly instead, matching the PR's own chosen defaults.
/// </summary>
internal class BlazorStaticSitePublishTarget(ILogger<BlazorStaticSitePublishTarget> logger)
    : AbstractAWSPublishTarget(logger)
{
    private const string ApiBehaviorPathPattern = "/puckdrop/*";
    private const string ApiBehaviorBasePath = "/puckdrop";

    public override string PublishTargetName => "S3 with CloudFront (Blazor)";

    public override Type PublishTargetAnnotation => typeof(PublishS3WithCloudFrontAnnotation);

    public override async Task GenerateConstructAsync(
        AWSCDKEnvironmentResource environment,
        IResource resource,
        IAWSPublishTargetAnnotation annotation,
        CancellationToken cancellationToken)
    {
        var publishAnnotation = annotation as PublishS3WithCloudFrontAnnotation
            ?? throw new InvalidOperationException(
                $"Annotation for resource '{resource.Name}' is not a valid {nameof(PublishS3WithCloudFrontAnnotation)}.");

        var config = publishAnnotation.Config;

        var projectDirectory = publishAnnotation.ProjectDirectory
            ?? throw new InvalidOperationException(
                $"Resource '{resource.Name}' is missing a project directory. " +
                $"Ensure PublishAsS3WithCloudFront() is called on a Blazor WASM project resource.");

        await PublishBlazorProjectAsync(resource.Name, projectDirectory, cancellationToken);

        if (Path.IsPathRooted(config.OutputPath))
            throw new InvalidOperationException(
                $"OutputPath must be a relative path, but got: '{config.OutputPath}'.");

        var buildOutputPath = Path.GetFullPath(Path.Combine(projectDirectory, config.OutputPath));

        var expectedRoot = projectDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!buildOutputPath.StartsWith(expectedRoot, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"OutputPath '{config.OutputPath}' resolves to '{buildOutputPath}', which is outside the project directory '{projectDirectory}'.");

        if (!Directory.Exists(buildOutputPath))
            throw new InvalidOperationException(
                $"Published output for '{resource.Name}' was not found at '{buildOutputPath}'. " +
                $"'dotnet publish' may have failed, or produced a different layout than expected.");

        FixIndexHtmlBaseHref(buildOutputPath);

        var context = CreatePublishTargetContext(environment);
        var stack = context.GetDeploymentStack<DeploymentStack>();

        // --- S3 Bucket ---
        var bucketProps = new BucketProps();
        config.PropsBucketCallback?.Invoke(context, bucketProps);
        ApplyBucketDefaults(bucketProps);

        var bucket = new Bucket(stack, $"Project-{resource.Name}-Bucket", bucketProps);
        config.ConstructBucketCallback?.Invoke(context, bucket);

        // --- CloudFront distribution ---
        // Built once and reused for both keys below - CloudFront's "/puckdrop/*" matches
        // "/puckdrop/foo" but not the bare "/puckdrop", so PR #203's own rule for any
        // trailing-"/*" backend pattern is to register the base path as a second, separate
        // behavior pointed at the same origin rather than construct it twice.
        var apiBehavior = CreateApiBehavior(stack);

        var distributionProps = new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(bucket),
                FunctionAssociations =
                [
                    new FunctionAssociation
                    {
                        EventType = FunctionEventType.VIEWER_REQUEST,
                        Function = CreateSpaFallbackFunction(stack, resource.Name),
                    },
                ],
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                [ApiBehaviorPathPattern] = apiBehavior,
                [ApiBehaviorBasePath] = apiBehavior,
            },
        };

        config.PropsDistributionCallback?.Invoke(context, distributionProps);
        ApplyDistributionDefaults(distributionProps);

        var distribution = new Distribution(stack, $"Project-{resource.Name}-Distribution", distributionProps);
        config.ConstructDistributionCallback?.Invoke(context, distribution);

        _ = new CfnOutput(stack, $"{resource.Name}-CloudFrontUrl", new CfnOutputProps
        {
            Value = $"https://{distribution.DomainName}",
        });

        // Cognito's UserPoolClient was created in DeploymentStack's constructor with a
        // placeholder callback/logout URL - the CloudFront domain didn't exist yet at that
        // point. Fix it up now via the CDK escape hatch.
        FixCognitoCallbackUrls(stack, distribution);

        // --- Bucket deployment ---
        var deploymentProps = new BucketDeploymentProps
        {
            Sources = [Source.Asset(buildOutputPath)],
            DestinationBucket = bucket,
            Distribution = distribution,
            DistributionPaths = ["/*"],
            // CDK's default (128 MB) starves the sync Lambda's CPU/network for a Blazor WASM
            // app's asset count (_framework/ alone commonly runs into the hundreds of files once
            // .br/.gz variants are counted) - confirmed directly from a real deploy's Lambda
            // logs, not just inferred: "Duration: 900000.00 ms ... Memory Size: 128 MB Max
            // Memory Used: 126 MB Status: timeout" - it ran flat out for its full 900s (15 min)
            // hard limit, pinned at 98% of its memory ceiling, with throughput visibly degrading
            // near the end (400 KiB/s -> 152 KiB/s) as memory pressure built up, and still had
            // ~192 of ~252+ files left when AWS killed it. CloudFormation's custom-resource
            // Provider framework then retries the whole thing from scratch. More memory gives
            // this Lambda proportionally more CPU/network, which should let a single invocation
            // actually finish instead of repeatedly timing out.
            MemoryLimit = 1024,
        };
        config.PropsBucketDeploymentCallback?.Invoke(context, deploymentProps);
        _ = new BucketDeployment(stack, $"Project-{resource.Name}-Deployment", deploymentProps);

        ApplyAWSLinkedObjectsAnnotation(environment, resource, distribution, this);
    }

    public override ReferenceConnectionInfo GetReferenceConnectionInfo(AWSLinkedObjectsAnnotation linkedAnnotation)
    {
        var result = new ReferenceConnectionInfo();
        if (linkedAnnotation.Construct is not Distribution distribution)
            return result;

        result.EnvironmentVariables = new Dictionary<string, string>
        {
            [$"services__{linkedAnnotation.Resource.Name}__https__0"] =
                Fn.Join("", ["https://", distribution.DomainName, "/"]),
        };
        return result;
    }

    public override IsDefaultPublishTargetMatchResult IsDefaultPublishTargetMatch(
        CDKDefaultsProvider cdkDefaultsProvider, IResource resource)
    {
        if (resource is BlazorWasmAppResource blazorResource)
        {
            return new IsDefaultPublishTargetMatchResult
            {
                IsMatch = true,
                PublishTargetAnnotation = new PublishS3WithCloudFrontAnnotation
                {
                    ProjectDirectory = blazorResource.ProjectDirectory,
                },
                Rank = IsDefaultPublishTargetMatchResult.DEFAULT_MATCH_RANK,
            };
        }

        return IsDefaultPublishTargetMatchResult.NO_MATCH;
    }

    /// <summary>
    /// The CloudFront behavior that proxies <c>/puckdrop/*</c> (+ the bare <c>/puckdrop</c>)
    /// through to the real, CDK-only API Gateway - baked in directly rather than resolved
    /// generically, since nothing in PuckDrop's Aspire resource graph wraps that API Gateway.
    /// </summary>
    private static BehaviorOptions CreateApiBehavior(DeploymentStack stack) => new()
    {
        Origin = new HttpOrigin($"{stack.HttpApi.Ref}.execute-api.{stack.Region}.amazonaws.com", new HttpOriginProps
        {
            ProtocolPolicy = OriginProtocolPolicy.HTTPS_ONLY,
        }),
        AllowedMethods = AllowedMethods.ALLOW_ALL,
        CachePolicy = CachePolicy.CACHING_DISABLED,
        OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
        ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
    };

    /// <summary>
    /// Sets the Cognito user pool client's real OAuth callback/logout URLs now that the
    /// CloudFront distribution's domain is known, via the CDK "escape hatch"
    /// (<c>Node.DefaultChild</c>) - the standard CDK pattern for mutating an L1 CloudFormation
    /// resource's properties after its owning L2 construct was already built. Property names
    /// (<c>CallbackUrLs</c>/<c>LogoutUrLs</c>) confirmed via reflection against the installed
    /// Amazon.CDK.Lib 2.262.0 - an unusual JSII-codegen casing, not a typo.
    /// </summary>
    private static void FixCognitoCallbackUrls(DeploymentStack stack, Distribution distribution)
    {
        if (stack.UserPoolClient.Node.DefaultChild is not CfnUserPoolClient cfnUserPoolClient)
            throw new InvalidOperationException(
                "Expected UserPoolClient's default child to be a CfnUserPoolClient - " +
                "Amazon.CDK.Lib's Cognito construct shape may have changed.");

        cfnUserPoolClient.CallbackUrLs =
            [Fn.Join("", ["https://", distribution.DomainName, "/authentication/login-callback"])];
        cfnUserPoolClient.LogoutUrLs =
            [Fn.Join("", ["https://", distribution.DomainName, "/"])];
    }

    private static void ApplyBucketDefaults(BucketProps props)
    {
        props.BlockPublicAccess ??= BlockPublicAccess.BLOCK_ALL;
        if (!props.EnforceSSL.HasValue)
            props.EnforceSSL = true;
    }

    private static void ApplyDistributionDefaults(DistributionProps props)
    {
        // A harmless default for the bare-root case; the real SPA-fallback logic lives in the
        // CloudFront Function below, not here - see its own comment for why.
        props.DefaultRootObject ??= "index.html";

        if (props.DefaultBehavior is BehaviorOptions behavior)
        {
            behavior.ViewerProtocolPolicy ??= ViewerProtocolPolicy.REDIRECT_TO_HTTPS;
            behavior.CachePolicy ??= CachePolicy.CACHING_OPTIMIZED;
        }
    }

    /// <summary>
    /// SPA fallback (serve index.html for any client-side route, so the Blazor router - not
    /// S3/CloudFront - decides what to render) deliberately implemented as a viewer-request
    /// CloudFront Function on the default behavior only, NOT as the distribution's
    /// CustomErrorResponses (403/404 -&gt; 200 index.html). CustomErrorResponses is a
    /// distribution-WIDE setting in CloudFront - it isn't scoped per-behavior - so it was also
    /// silently rewriting genuine 403/404 responses from the API behavior (e.g. a real
    /// [Authorize(Policy = "AdminPolicy")] 403) into a fake 200 response with index.html's HTML
    /// body. The Blazor client's JSON deserialization then choked on that HTML ("Unexpected
    /// token '<'" / ExpectedStartOfValueNotFound - confirmed live, via a real deployed
    /// distribution). A CloudFront Function attached to just the default behavior only ever
    /// touches requests actually routed to S3, leaving the API behavior's real status codes and
    /// bodies untouched.
    /// </summary>
    private static IFunction CreateSpaFallbackFunction(DeploymentStack stack, string resourceName) =>
        new Amazon.CDK.AWS.CloudFront.Function(stack, $"Project-{resourceName}-SpaFallback", new FunctionProps
        {
            Runtime = FunctionRuntime.JS_2_0,
            Code = FunctionCode.FromInline(
                """
                function handler(event) {
                    var request = event.request;
                    // Any URI without a "." is treated as a client-side route (Blazor's own
                    // router decides what to render) rather than a real static file - every
                    // actual asset this app serves (js/css/wasm/png/etc.) has an extension.
                    if (!request.uri.includes('.')) {
                        request.uri = '/index.html';
                    }
                    return request;
                }
                """),
        });

    /// <summary>
    /// PuckDrop.Web.csproj's checked-in wwwroot/index.html hardcodes &lt;base href="/web/" /&gt;
    /// (matching its local-dev StaticWebAssetBasePath, for blazor-gateway) - that's a plain,
    /// static HTML file, entirely independent of the -p:PublishForRootStaticWebAssets=true flag
    /// passed above, which only affects the physical directory layout, not this tag's content
    /// (confirmed directly: overriding StaticWebAssetBasePath alone left it unchanged). The
    /// deployed site is served from its own CloudFront origin root with no sub-path convention
    /// to match, so rewrite it here, post-publish, rather than touch the shared source file that
    /// local dev still needs at "/web/". The pre-compressed index.html.br/.gz siblings dotnet
    /// publish also produces are left as-is deliberately - nothing in this target's CloudFront
    /// config serves them (no CloudFront Function/Lambda@Edge rewrite maps a request to its
    /// compressed sibling by suffix), so their now-stale base href is inert, never actually
    /// requested.
    /// </summary>
    private static void FixIndexHtmlBaseHref(string buildOutputPath)
    {
        var indexHtmlPath = Path.Combine(buildOutputPath, "index.html");
        if (!File.Exists(indexHtmlPath))
            return;

        var html = File.ReadAllText(indexHtmlPath);
        var fixedHtml = Regex.Replace(html, """<base\s+href="[^"]*"\s*/?>""", """<base href="/" />""");
        if (fixedHtml != html)
            File.WriteAllText(indexHtmlPath, fixedHtml);
    }

    /// <summary>
    /// Runs `dotnet publish -c Release` on the Blazor project - the equivalent of PR #203's
    /// `IStaticSiteBuilder`/`DefaultStaticSiteBuilder` running `npm run build`, for the same
    /// reason: don't assume implicit build ordering already produced the optimized output,
    /// invoke the real publish step ourselves.
    /// </summary>
    private async Task PublishBlazorProjectAsync(string resourceName, string projectDirectory, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Publishing Blazor WASM project '{ResourceName}' via 'dotnet publish -c Release' in '{ProjectDirectory}'",
            resourceName, projectDirectory);

        // -p:PublishForRootStaticWebAssets=true opts PuckDrop.Web.csproj's StaticWebAssetBasePath
        // out of its local-dev "web" value (matching the blazor-gateway resource name) - this
        // deployment is served from its own CloudFront origin root, with no gateway sub-path
        // convention to match. A plain assignment in the csproj would otherwise silently win over
        // any command-line override regardless of value - confirmed directly, which is why that
        // property carries its own Condition rather than defaulting unconditionally.
        var startInfo = new ProcessStartInfo("dotnet", "publish -c Release -p:PublishForRootStaticWebAssets=true")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'dotnet publish' for '{resourceName}'.");

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Logger.LogInformation("{Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Logger.LogWarning("{Line}", e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet publish -c Release' for '{resourceName}' failed with exit code {process.ExitCode} " +
                $"in '{projectDirectory}'.");
    }
}
