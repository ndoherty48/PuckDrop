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
/// Publishes the Blazor WebAssembly UI to an S3 bucket behind CloudFront, with <c>/puckdrop/*</c>
/// routed to the API Gateway and a SPA fallback to <c>index.html</c>.
///
/// Adapted from AWS's unreleased <c>S3StaticWebsitePublishTarget</c>
/// (aws/integrations-on-dotnet-aspire-for-aws#203), so it can be swapped out if that ships. The
/// differences: it runs <c>dotnet publish</c>, bakes in the API behavior from
/// <see cref="DeploymentStack.HttpApi"/>, and fixes the Cognito callback URLs once the CloudFront
/// domain is known. Constructs go in <see cref="DeploymentStack"/> because Aspire's own CDK stack
/// is internal.
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
        // "/puckdrop/*" doesn't match the bare "/puckdrop", so both paths share one behavior.
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

        // The CloudFront domain didn't exist when DeploymentStack created the Cognito client.
        FixCognitoCallbackUrls(stack, distribution);

        // --- Bucket deployment ---
        var deploymentProps = new BucketDeploymentProps
        {
            Sources = [Source.Asset(buildOutputPath)],
            DestinationBucket = bucket,
            Distribution = distribution,
            DistributionPaths = ["/*"],
            // The 128 MB default timed out syncing Blazor's hundreds of files; more memory
            // means more CPU and network.
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
    /// Proxies <c>/puckdrop</c> to the API Gateway, which isn't an Aspire resource.
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
    /// Sets the Cognito client's real callback and logout URLs through the CDK escape hatch.
    /// <c>CallbackUrLs</c>/<c>LogoutUrLs</c> is JSII's casing, not a typo.
    /// </summary>
    private static void FixCognitoCallbackUrls(DeploymentStack stack, Distribution distribution)
    {
        if (stack.UserPoolClient.Node.DefaultChild is not CfnUserPoolClient cfnUserPoolClient)
            throw new InvalidOperationException(
                "Expected UserPoolClient's default child to be a CfnUserPoolClient - " +
                "Amazon.CDK.Lib's Cognito construct shape may have changed.");

        cfnUserPoolClient.CallbackUrLs =
            [Fn.Join("", ["https://", distribution.DomainName, "/authentication/login-callback"])];
        // Must match the logout_uri MainLayout.Logout sends, or Cognito redirects to /login.
        cfnUserPoolClient.LogoutUrLs =
            [Fn.Join("", ["https://", distribution.DomainName, "/authentication/logged-out"])];
    }

    private static void ApplyBucketDefaults(BucketProps props)
    {
        props.BlockPublicAccess ??= BlockPublicAccess.BLOCK_ALL;
        if (!props.EnforceSSL.HasValue)
            props.EnforceSSL = true;
    }

    private static void ApplyDistributionDefaults(DistributionProps props)
    {
        // SPA routing is handled by the CloudFront Function below.
        props.DefaultRootObject ??= "index.html";

        if (props.DefaultBehavior is BehaviorOptions behavior)
        {
            behavior.ViewerProtocolPolicy ??= ViewerProtocolPolicy.REDIRECT_TO_HTTPS;
            behavior.CachePolicy ??= CachePolicy.CACHING_OPTIMIZED;
            // The publish skips .br/.gz files, so CloudFront compresses on the fly instead.
            if (!behavior.Compress.HasValue)
                behavior.Compress = true;
        }
    }

    /// <summary>
    /// Serves index.html for client-side routes. A CloudFront Function on the default behavior
    /// rather than CustomErrorResponses, which apply distribution-wide and turned the API's real
    /// 403/404s into HTML.
    /// </summary>
    private static IFunction CreateSpaFallbackFunction(DeploymentStack stack, string resourceName) =>
        new Amazon.CDK.AWS.CloudFront.Function(stack, $"Project-{resourceName}-SpaFallback", new FunctionProps
        {
            Runtime = FunctionRuntime.JS_2_0,
            Code = FunctionCode.FromInline(
                """
                function handler(event) {
                    var request = event.request;
                    // Every real asset has an extension; anything else is a Blazor route.
                    if (!request.uri.includes('.')) {
                        request.uri = '/index.html';
                    }
                    return request;
                }
                """),
        });

    /// <summary>
    /// index.html hardcodes <c>&lt;base href="/web/" /&gt;</c> for local dev under blazor-gateway;
    /// the deployed site is served from the root.
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

    private async Task PublishBlazorProjectAsync(string resourceName, string projectDirectory, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Publishing Blazor WASM project '{ResourceName}' via 'dotnet publish -c Release' in '{ProjectDirectory}'",
            resourceName, projectDirectory);

        // PublishForRootStaticWebAssets: serve from the root, not the local-dev "web" base path.
        // EnableDefaultCompressionFormats=false: skip unused .br/.gz files (CloudFront compresses).
        // WasmBuildNative=false: skip the Emscripten relink, which breaks on SDK paths containing
        // spaces (e.g. ~/Library/Application Support/dotnet) when wasm-tools is installed.
        var startInfo = new ProcessStartInfo("dotnet",
            "publish -c Release -p:PublishForRootStaticWebAssets=true -p:EnableDefaultCompressionFormats=false -p:WasmBuildNative=false")
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
