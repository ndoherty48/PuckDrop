#pragma warning disable ASPIREAWSPUBLISHERS001

using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Deployment;
using Aspire.Hosting.AWS.Deployment;

namespace PuckDrop.AppHost.AWS.Deployment;

/// <summary>
/// Configuration for publishing the Blazor WebAssembly UI as an S3-backed static website
/// fronted by a CloudFront distribution. Property-for-property mirrors the shape of AWS's own
/// (unreleased, as of aws/integrations-on-dotnet-aspire-for-aws#203) JavaScript-app equivalent,
/// so a future swap to an official implementation is close to a no-op.
/// </summary>
public class PublishS3WithCloudFrontConfig
{
    /// <summary>
    /// The relative path from the Blazor project's directory to the published static output -
    /// the <c>wwwroot</c> folder <c>dotnet publish</c> produces. Defaults to the standard
    /// MSBuild publish convention for this repo's target framework.
    /// </summary>
    public string OutputPath { get; set; } = Path.Combine("bin", "Release", "net10.0", "publish", "wwwroot");

    /// <summary>Callback to modify the <see cref="BucketProps"/> before the S3 bucket is created.</summary>
    public PublishCallback<BucketProps>? PropsBucketCallback { get; set; }

    /// <summary>Callback to modify the created <see cref="Bucket"/> construct.</summary>
    public PublishCallback<Bucket>? ConstructBucketCallback { get; set; }

    /// <summary>Callback to modify the <see cref="BucketDeploymentProps"/> before the deployment is created.</summary>
    public PublishCallback<BucketDeploymentProps>? PropsBucketDeploymentCallback { get; set; }

    /// <summary>Callback to modify the <see cref="DistributionProps"/> before the CloudFront distribution is created.</summary>
    public PublishCallback<DistributionProps>? PropsDistributionCallback { get; set; }

    /// <summary>Callback to modify the created <see cref="Distribution"/> construct.</summary>
    public PublishCallback<Distribution>? ConstructDistributionCallback { get; set; }
}
