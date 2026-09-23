using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Layout;
using PuckDrop.Web.Services;
using Xunit;

namespace PuckDrop.Web.Tests;

public class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        Services.AddSingleton(new AuthConfigModel("http://localhost/realms/PuckDrop", "PuckDrop-UI", "code"));
        Services.AddSingleton(new ConfirmDialogService());
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(l => l.Body, "<p>Page body</p>"));

    private void NavigateTo(string relativeUri) =>
        Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUri);

    [Fact]
    public void Friend_SeesPlayerNavOnly_WithNameInitialsAndLogOut()
    {
        AddAuthorization().SetAuthorized("Friend User");

        var cut = RenderLayout();

        Assert.Equal(new[] { "Home", "Leaderboard", "History" }, cut.FindAll(".pd-nav a").Select(a => a.TextContent.Trim()));
        Assert.Equal(new[] { "Home", "Leaderboard", "History" }, cut.FindAll(".pd-tabbar a").Select(a => a.TextContent.Trim()));
        Assert.Equal("Friend User", cut.Find(".pd-account-name").TextContent);
        Assert.Equal("FU", cut.Find(".pd-account > .pd-avatar").TextContent);
        Assert.Equal("Log out", cut.Find(".pd-account .pd-btn").TextContent.Trim());
    }

    [Fact]
    public void Admin_SeesAdminInBothNavs()
    {
        var authContext = AddAuthorization();
        authContext.SetAuthorized("Admin User");
        authContext.SetRoles("admin");

        var cut = RenderLayout();

        Assert.Contains(cut.FindAll(".pd-nav a"), a => a.TextContent.Trim() == "Admin");
        Assert.Contains(cut.FindAll(".pd-tabbar a"), a => a.TextContent.Trim() == "Admin");
    }

    [Fact]
    public void Anonymous_SeesLogIn_AndNoAccountDetails()
    {
        AddAuthorization();

        var cut = RenderLayout();

        Assert.Empty(cut.FindAll(".pd-account"));
        Assert.Contains(cut.FindAll("header button"), b => b.TextContent.Trim() == "Log in");
    }

    [Theory]
    [InlineData("", "Home")]
    [InlineData("poll/p1", "Home")]
    [InlineData("leaderboard", "Leaderboard")]
    [InlineData("results/p1", "History")]
    public void CurrentSection_IsMarkedWithAriaCurrent_InBothNavs(string relativeUri, string expectedLabel)
    {
        AddAuthorization().SetAuthorized("Friend User");
        NavigateTo(relativeUri);

        var cut = RenderLayout();

        var topCurrent = Assert.Single(cut.FindAll(".pd-nav a[aria-current='page']"));
        var tabCurrent = Assert.Single(cut.FindAll(".pd-tabbar a[aria-current='page']"));
        Assert.Equal(expectedLabel, topCurrent.TextContent.Trim());
        Assert.Equal(expectedLabel, tabCurrent.TextContent.Trim());
    }

    [Fact]
    public void AccountButton_TogglesMenu_AndNavigatingClosesIt()
    {
        AddAuthorization().SetAuthorized("Friend User");
        var cut = RenderLayout();

        Assert.Equal("false", cut.Find(".pd-account-button").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("#pd-account-menu"));

        cut.Find(".pd-account-button").Click();

        Assert.Equal("true", cut.Find(".pd-account-button").GetAttribute("aria-expanded"));
        Assert.Contains("Log out", cut.Find("#pd-account-menu").TextContent);

        NavigateTo("leaderboard");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#pd-account-menu")));
    }

    [Fact]
    public void SkipLink_KeepsTheCurrentPath_AndFocusesMain()
    {
        AddAuthorization().SetAuthorized("Friend User");
        NavigateTo("leaderboard");
        var cut = RenderLayout();

        var skipLink = cut.Find(".pd-skip-link");
        Assert.Equal("http://localhost/leaderboard#main", skipLink.GetAttribute("href"));

        skipLink.Click();

        JSInterop.VerifyFocusAsyncInvoke();
    }
}
