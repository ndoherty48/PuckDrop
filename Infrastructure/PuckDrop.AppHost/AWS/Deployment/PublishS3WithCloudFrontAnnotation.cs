#pragma warning disable ASPIREAWSPUBLISHERS001

using Aspire.Hosting.AWS.Deployment;

namespace PuckDrop.AppHost.AWS.Deployment;

/// <summary>
/// Marks a resource for publishing via <see cref="BlazorStaticSitePublishTarget"/>. Mirrors the
/// shape of AWS's own (unreleased) <c>PublishS3WithCloudFrontAnnotation</c> for JavaScript apps.
/// </summary>
internal class PublishS3WithCloudFrontAnnotation : IAWSPublishTargetAnnotation
{
    public PublishS3WithCloudFrontConfig Config { get; } = new();

    /// <summary>
    /// The published <c>wwwroot</c>, set by the build step <c>PublishAsS3WithCloudFront</c> adds.
    /// </summary>
    public string? PublishedWwwrootPath { get; set; }
}
