using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Components;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// The shared StatusScreen and the system screens built on it. Authentication.razor's states
/// aren't rendered here - they need the full RemoteAuthenticationService machinery (see AppTests'
/// note); LogoutTests covers the logged-out screen in the E2E suite.
/// </summary>
public class SystemScreensTests : BunitContext
{
    [Fact]
    public void StatusScreen_RendersDecorativeIcon_TitleAsH1_BodyAndActions()
    {
        var cut = Render<StatusScreen>(parameters => parameters
            .Add(s => s.IconName, "lock")
            .Add(s => s.Title, "You don't have access")
            .Add(s => s.ChildContent, "<p>Body text</p>")
            .Add(s => s.Actions, "<a href=\"\">Back to home</a>"));

        Assert.Equal("You don't have access", cut.Find("h1").TextContent.Trim());
        Assert.Equal("true", cut.Find(".pd-state-icon").GetAttribute("aria-hidden"));
        Assert.Contains("Body text", cut.Find(".pd-state-body").TextContent);
        Assert.Contains("Back to home", cut.Find(".pd-state-actions").TextContent);
    }

    [Fact]
    public void Unauthorized_ExplainsWhy_AndLinksHome()
    {
        var cut = Render<Unauthorized>();

        Assert.Equal("You don't have access", cut.Find("h1").TextContent.Trim());
        Assert.Contains("You don't have permission to view this page.", cut.Find(".pd-state-body").TextContent);

        var link = cut.Find(".pd-state-actions a");
        Assert.Equal("", link.GetAttribute("href"));
        Assert.Equal("Back to home", link.TextContent.Trim());
    }

    [Fact]
    public void NotFound_SaysSo_AndLinksHome()
    {
        var cut = Render<NotFound>();

        Assert.Equal("Page not found", cut.Find("h1").TextContent.Trim());
        Assert.Equal("Back to home", cut.Find(".pd-state-actions a").TextContent.Trim());
    }

    [Fact]
    public void AppLoadFailure_ShowsBrandHeader_ErrorDetail_AndTryAgain()
    {
        Services.AddSingleton(new AuthConfigLoadResult(false, "Could not reach the server: timed out"));

        var cut = Render<PuckDrop.Web.App>();

        Assert.Contains("PuckDrop", cut.Find("header .pd-brand").TextContent);
        Assert.Equal("Couldn't reach the server", cut.Find("h1").TextContent.Trim());
        Assert.Contains("Please try again later.", cut.Find(".pd-state-body").TextContent);
        Assert.Contains("Could not reach the server: timed out", cut.Find(".pd-state-detail").TextContent);
        Assert.Equal("Try again", cut.Find(".pd-state-actions a").TextContent.Trim());
    }
}
