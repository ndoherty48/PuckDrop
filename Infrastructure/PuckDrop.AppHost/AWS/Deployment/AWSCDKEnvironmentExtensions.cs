#pragma warning disable ASPIREAWSPUBLISHERS001
#pragma warning disable ASPIREBLAZOR001
#pragma warning disable ASPIREPIPELINES001

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace PuckDrop.AppHost.AWS.Deployment;

/// <summary>
/// PuckDrop's own extension mirroring the calling convention of AWS's (unreleased)
/// <c>PublishAsS3WithCloudFront</c> for JavaScript apps, adapted for Blazor WASM.
/// </summary>
public static class AWSCDKEnvironmentExtensions
{
    /// <summary>
    /// Publishes this Blazor WASM project to S3 + CloudFront via
    /// <see cref="BlazorStaticSitePublishTarget"/>, which must also be registered as an
    /// <c>IAWSPublishTarget</c>.
    /// </summary>
    public static IResourceBuilder<BlazorWasmAppResource> PublishAsS3WithCloudFront(
        this IResourceBuilder<BlazorWasmAppResource> builder,
        Action<PublishS3WithCloudFrontConfig>? configure = null)
    {
        var annotation = new PublishS3WithCloudFrontAnnotation();
        configure?.Invoke(annotation.Config);

        builder.WithAnnotation(annotation);

        // AddBlazorWasmProject excludes itself from publishing, so the CDK step would skip it.
        // Replacing that marker with a no-op callback includes it again.
        builder.WithManifestPublishingCallback(_ => { });

        // Publish in its own build step, like the Lambda packaging, so it runs alongside it and
        // finishes before the CDK step (which depends on "build") reads the output.
        var resource = builder.Resource;
        builder.WithPipelineStepFactory(
            $"build-{resource.Name}-static-site",
            async context => annotation.PublishedWwwrootPath = await BlazorWasmPublisher.PublishAsync(
                resource.ProjectPath,
                Path.Combine(Path.GetTempPath(), "puckdrop-aspire", resource.Name),
                context.Logger,
                context.CancellationToken),
            dependsOn: [WellKnownPipelineSteps.BuildPrereq],
            requiredBy: [WellKnownPipelineSteps.Build],
            tags: [WellKnownPipelineTags.BuildCompute],
            description: $"Publishes the Blazor WASM app '{resource.Name}' for S3.");

        if(builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            builder.ApplicationBuilder.OnBeforeStart((_, _) =>
            {
                var appBuilder = builder.ApplicationBuilder;

                if (builder.Resource.Parent is { } gateway)
                    appBuilder.CreateResourceBuilder(gateway).ExcludeFromManifest();

                var companionName = $"{builder.Resource.Name}publish";
                if (appBuilder.Resources.FirstOrDefault(r => r.Name == companionName) is { } companion)
                    appBuilder.CreateResourceBuilder(companion).ExcludeFromManifest();

                return Task.CompletedTask;
            });
        }

        return builder;
    }
}
