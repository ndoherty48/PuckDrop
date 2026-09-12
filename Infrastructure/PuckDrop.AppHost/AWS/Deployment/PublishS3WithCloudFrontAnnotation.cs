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
    /// Directory of the Blazor project, captured at registration time.
    /// </summary>
    public string? ProjectDirectory { get; set; }
}
