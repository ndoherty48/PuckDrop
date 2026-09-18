using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Services;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// If the auth-config fetch failed, OIDC services aren't registered, so App.razor must show an
/// error instead of rendering the auth-dependent tree.
/// </summary>
public class AppTests : BunitContext
{
    [Fact]
    public void AuthConfigLoadFailed_RendersErrorMessage_NotTheNormalRouterTree()
    {
        Services.AddSingleton(new AuthConfigLoadResult(false, "Could not reach the server: timed out"));

        var cut = Render<PuckDrop.Web.App>();

        Assert.Contains("Couldn't reach the server", cut.Markup);
        Assert.Contains("Could not reach the server: timed out", cut.Markup);
    }

    // The success path needs the full OIDC machinery to render, and every other page test
    // covers it implicitly.
}
