#pragma warning disable ASPIREAWSPUBLISHERS001
#pragma warning disable ASPIREBLAZOR001

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace PuckDrop.AppHost.AWS.Deployment;

/// <summary>
/// PuckDrop's own extension mirroring the calling convention of AWS's (unreleased)
/// <c>PublishAsS3WithCloudFront</c> for JavaScript apps, adapted for Blazor WASM.
/// </summary>
public static class AWSCDKEnvironmentExtensions
{
    /// <summary>
    /// Publishes this Blazor WASM project as an S3 bucket fronted by a CloudFront distribution
    /// (see <see cref="BlazorStaticSitePublishTarget"/>) when the AWS CDK environment is
    /// published. Requires <c>builder.Services.AddTransient&lt;IAWSPublishTarget,
    /// BlazorStaticSitePublishTarget&gt;()</c> to be registered separately - this only adds the
    /// annotation the registered target looks for.
    /// </summary>
    public static IResourceBuilder<BlazorWasmAppResource> PublishAsS3WithCloudFront(
        this IResourceBuilder<BlazorWasmAppResource> builder,
        Action<PublishS3WithCloudFrontConfig>? configure = null)
    {
        var annotation = new PublishS3WithCloudFrontAnnotation
        {
            ProjectDirectory = builder.Resource.ProjectDirectory,
        };
        configure?.Invoke(annotation.Config);

        builder.WithAnnotation(annotation);

        // AddBlazorWasmProject calls ExcludeFromManifest() on itself internally (Aspire.Hosting.
        // Blazor's own default expectation is that a Blazor WASM app publishes via its gateway's
        // container-companion mechanism, not a generic AWS publish target) - and
        // CDKPublishingStep.ProcessResources skips any resource where IsExcludedFromPublish() is
        // true, before it ever looks at annotations like the one just added above. Confirmed via
        // a real `aspire deploy` attempt: no S3 bucket/CloudFront distribution were created, and
        // the deploy log never mentioned "web" or BlazorStaticSitePublishTarget at all.
        // WithManifestPublishingCallback replaces the ExcludeFromManifest sentinel with a fresh
        // (non-Ignore) annotation, which un-excludes it - the callback itself is a no-op since
        // the CDK publish path doesn't read the JSON manifest.
        builder.WithManifestPublishingCallback(_ => { });

        return builder;
    }
}
