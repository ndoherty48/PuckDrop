using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PuckDrop.AppHost.AWS.Deployment;

/// <summary>
/// Runs <c>dotnet publish</c> for a Blazor WASM project and prepares its <c>wwwroot</c> for S3.
/// </summary>
internal static class BlazorWasmPublisher
{
    /// <summary>
    /// Publishes into a freshly emptied <paramref name="outputDirectory"/>, so files removed from
    /// the app never linger in the upload, and returns the published <c>wwwroot</c> path.
    /// </summary>
    public static async Task<string> PublishAsync(
        string projectPath, string outputDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);

        logger.LogInformation("Publishing Blazor WASM project '{ProjectPath}' to '{OutputDirectory}'", projectPath, outputDirectory);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);
        // Serve from the root, not the local-dev "web" base path.
        startInfo.ArgumentList.Add("-p:PublishForRootStaticWebAssets=true");
        // Skip unused .br/.gz files; CloudFront compresses on the fly.
        startInfo.ArgumentList.Add("-p:EnableDefaultCompressionFormats=false");
        // Skip the Emscripten relink, which breaks on SDK paths containing spaces (e.g.
        // ~/Library/Application Support/dotnet) when wasm-tools is installed.
        startInfo.ArgumentList.Add("-p:WasmBuildNative=false");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'dotnet publish' for '{projectPath}'.");

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("{Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("{Line}", e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"'dotnet publish' for '{projectPath}' failed with exit code {process.ExitCode}.");

        var wwwrootPath = Path.Combine(outputDirectory, "wwwroot");
        if (!Directory.Exists(wwwrootPath))
            throw new InvalidOperationException(
                $"'dotnet publish' for '{projectPath}' succeeded but produced no wwwroot at '{wwwrootPath}'.");

        FixIndexHtmlBaseHref(wwwrootPath);
        return wwwrootPath;
    }

    /// <summary>
    /// index.html hardcodes <c>&lt;base href="/web/" /&gt;</c> for local dev under blazor-gateway;
    /// the deployed site is served from the root.
    /// </summary>
    private static void FixIndexHtmlBaseHref(string wwwrootPath)
    {
        var indexHtmlPath = Path.Combine(wwwrootPath, "index.html");
        if (!File.Exists(indexHtmlPath))
            return;

        var html = File.ReadAllText(indexHtmlPath);
        var fixedHtml = Regex.Replace(html, """<base\s+href="[^"]*"\s*/?>""", """<base href="/" />""");
        if (fixedHtml != html)
            File.WriteAllText(indexHtmlPath, fixedHtml);
    }
}
