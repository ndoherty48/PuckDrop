using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// The current-user highlight must match the raw "sub" claim: Blazor WASM doesn't map it to
/// ClaimTypes.NameIdentifier the way the server does.
/// </summary>
public class LeaderboardTests : BunitContext
{
    // Deliberately out of rank order - the page sorts by Rank.
    private static LeaderboardModel BuildLeaderboard() => new("2025-26",
    [
        LeaderboardEntries.Entry("user-2", "Friend", 8, 12, 2),
        LeaderboardEntries.Entry("user-1", "Nathan", 10, 12, 1)
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

    /// <summary>
    /// A leaderboard where one player carries a deduction that put them below zero, and another
    /// had a game day voided.
    /// </summary>
    private IRenderedComponent<Leaderboard> RenderWithSanctions()
    {
        var leaderboard = new LeaderboardModel("2025-26",
        [
            LeaderboardEntries.Entry("user-1", "Nathan", totalPoints: 10, totalAnswered: 12, rank: 1),
            LeaderboardEntries.Entry(
                "user-2", "Friend", totalPoints: 4, totalAnswered: 6, rank: 2, earnedPoints: 4,
                voids: [new LeaderboardVoidModel("poll-9", "Giants vs Steelers", "No-show")]),
            LeaderboardEntries.Entry(
                "user-3", "Rory", totalPoints: -3, totalAnswered: 4, rank: 3, earnedPoints: 2,
                adjustments: [new LeaderboardAdjustmentModel("adj-1", -5, "Picked after puck drop")])
        ]);

        var handler = new RoutingHttpMessageHandler().MapJson(HttpMethod.Get, "leaderboard", leaderboard);
        Services.AddSingleton(handler.BuildClient());

        var authContext = AddAuthorization();
        authContext.SetAuthorized("Test User");

        return Render<Leaderboard>();
    }

    [Fact]
    public void ADeduction_IsShownWithItsReason_NextToThePlayersName()
    {
        // Public by design: a penalty nobody can see the reason for is what starts the argument.
        var cut = RenderWithSanctions();

        cut.WaitForAssertion(() =>
        {
            var row = cut.FindAll("tbody tr")[2];
            Assert.Contains("Picked after puck drop", row.TextContent);
            Assert.Contains("\u22125", row.TextContent); // real minus sign, not a hyphen
        });
    }

    [Fact]
    public void AVoidedGameDay_IsShownWithItsReasonAndTheGameDay()
    {
        var cut = RenderWithSanctions();

        cut.WaitForAssertion(() =>
        {
            var row = cut.FindAll("tbody tr")[1];
            Assert.Contains("Giants vs Steelers", row.TextContent);
            Assert.Contains("No-show", row.TextContent);
        });
    }

    [Fact]
    public void ANegativeTotal_RendersWithARealMinusSign()
    {
        var cut = RenderWithSanctions();

        cut.WaitForAssertion(() =>
            Assert.Equal("\u22123", cut.FindAll("tbody tr")[2].QuerySelector(".lb-points")!.TextContent.Trim()));
    }

    [Fact]
    public void Accuracy_IgnoresDeductions_SoAPenaltyCannotShowANegativeHitRate()
    {
        // Rory earned 2 of 4 but carries a -5 deduction: the hit rate is still 50%.
        var cut = RenderWithSanctions();

        cut.WaitForAssertion(() =>
        {
            var cells = cut.FindAll("tbody tr")[2].QuerySelectorAll("td");
            Assert.Equal("50.0%", cells[^1].TextContent.Trim());
        });
    }

    [Fact]

    public void CurrentUsersRow_GetsHighlightedAndYouBadge()
    {
        var cut = RenderWithCurrentUser(currentUserSub: "user-1");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Contains("pd-row-you", rows[0].GetAttribute("class"));
            Assert.Contains("You", rows[0].TextContent);

            Assert.DoesNotContain("pd-row-you", rows[1].GetAttribute("class") ?? "");
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
            Assert.All(rows, row => Assert.DoesNotContain("pd-row-you", row.GetAttribute("class") ?? ""));
            Assert.DoesNotContain("You", cut.Markup);
        });
    }

    [Fact]
    public void RowsFollowRank_WithPointsAnsweredAndAccuracy()
    {
        var cut = RenderWithCurrentUser(currentUserSub: null);

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Equal(2, rows.Count);

            Assert.Equal("Nathan", rows[0].QuerySelector(".lb-name")!.TextContent.Trim());
            Assert.Equal(
                new[] { "1", "10", "12", "83.3%" },
                rows[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()));
            Assert.Equal("Friend", rows[1].QuerySelector(".lb-name")!.TextContent.Trim());
        });
    }
}
