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
        return builder;
    }
}
