using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// Regression coverage for a real bug fixed earlier in this project's history: the current-user
/// row highlight has to match the raw "sub" claim, not ClaimTypes.NameIdentifier - Blazor WASM's
/// claims factory doesn't remap claim types the way server-side ASP.NET Core does, so the two
/// only look equivalent, and using the wrong one silently never highlights any row.
/// </summary>
public class LeaderboardTests : BunitContext
{
    private static LeaderboardModel BuildLeaderboard() => new("2025-26",
    [
        new LeaderboardEntryModel("user-1", "Nathan", 10, 12, 1),
        new LeaderboardEntryModel("user-2", "Friend", 8, 12, 2)
    ]);

    private IRenderedComponent<Leaderboard> RenderWithCurrentUser(string? currentUserSub)
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "leaderboard", BuildLeaderboard());
        Services.AddSingleton(handler.BuildClient());

        var authContext = AddAuthorization();
        authContext.SetAuthorized("Test User");
        if (currentUserSub is not null)
            authContext.SetClaims(new Claim("sub", currentUserSub));

        return Render<Leaderboard>();
    }

    [Fact]
    public void CurrentUsersRow_GetsHighlightedAndYouBadge()
    {
        var cut = RenderWithCurrentUser(currentUserSub: "user-1");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Contains("table-primary", rows[0].GetAttribute("class"));
            Assert.Contains("You", rows[0].TextContent);

            Assert.DoesNotContain("table-primary", rows[1].GetAttribute("class") ?? "");
            Assert.DoesNotContain("You", rows[1].TextContent);
        });
    }

    [Fact]
    public void NoRowMatchesCurrentUser_NoRowIsHighlighted()
    {
        var cut = RenderWithCurrentUser(currentUserSub: "someone-else");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.All(rows, row => Assert.DoesNotContain("table-primary", row.GetAttribute("class") ?? ""));
            Assert.DoesNotContain("You", cut.Markup);
        });
    }
}
