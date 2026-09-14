using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

public class HomeTests : BunitContext
{
    private static readonly SeasonModel Season =
        new("2026-27", "EIHL 2026–27", new DateOnly(2026, 8, 29), new DateOnly(2027, 4, 11));

    private static PollModel OpenPoll() => new(
        "poll-1", Season.SeasonId, new DateOnly(2026, 9, 19), "Belfast Giants vs Sheffield Steelers",
        DateTime.UtcNow.AddDays(5).AddHours(1), "Open", "admin", DateTime.UtcNow);

    private static LeaderboardModel BuildLeaderboard() => new(Season.SeasonId,
    [
        new LeaderboardEntryModel("user-6", "Rory N.", 6, 12, 6),
        new LeaderboardEntryModel("user-1", "Ciara D.", 10, 12, 1),
        new LeaderboardEntryModel("user-3", "Sam R.", 8, 12, 3),
        new LeaderboardEntryModel("user-2", "Jonny M.", 9, 12, 2),
        new LeaderboardEntryModel("user-5", "Mark K.", 7, 12, 5),
        new LeaderboardEntryModel("user-4", "Aoife B.", 7, 9, 4)
    ]);

    private static RoutingHttpMessageHandler Routes(PollModel? activePoll, List<UserAnswerModel> existingAnswers)
    {
        var activePolls = activePoll is null ? new List<PollModel>() : new List<PollModel> { activePoll };

        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "seasons/current", Season)
            .MapJson(HttpMethod.Get, $"polls/active?seasonId={Season.SeasonId}", activePolls)
            .MapJson(HttpMethod.Get, $"leaderboard?seasonId={Season.SeasonId}", BuildLeaderboard());

        if (activePoll is not null)
            handler.MapJson(HttpMethod.Get, $"polls/{activePoll.PollId}/answers", existingAnswers);

        return handler;
    }

    private IRenderedComponent<Home> RenderHome(RoutingHttpMessageHandler handler, string currentUserSub = "user-3")
    {
        Services.AddSingleton(handler.BuildClient());

        var authContext = AddAuthorization();
        authContext.SetAuthorized("Sam R.");
        authContext.SetClaims(new Claim("sub", currentUserSub));

        return Render<Home>();
    }

    [Fact]
    public void OpenPoll_NoPicksYet_ShowsTitleDeadlineAndMakeYourPicks()
    {
        var cut = RenderHome(Routes(OpenPoll(), []));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Belfast Giants vs Sheffield Steelers", cut.Find("#next-game-title").TextContent.Trim());
            Assert.Contains("In 5 days", cut.Find(".home-facts").TextContent);
            Assert.Contains("Not in yet", cut.Find(".home-facts").TextContent);

            var cta = cut.Find("a.home-cta");
            Assert.Equal("poll/poll-1", cta.GetAttribute("href"));
            Assert.Equal("Make your picks", cta.TextContent.Trim());
        });
    }

    [Fact]
    public void OpenPoll_PicksAlreadySubmitted_ShowsSubmittedAndUpdateYourPicks()
    {
        var cut = RenderHome(Routes(OpenPoll(), [new UserAnswerModel("q-1", "o-1", DateTime.UtcNow, null)]));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Picks submitted", cut.Find(".home-facts").TextContent);
            Assert.Equal("Update your picks", cut.Find("a.home-cta").TextContent.Trim());
        });
    }

    [Fact]
    public void NoActivePoll_ShowsNoUpcomingPoll_WithoutACallToAction()
    {
        var cut = RenderHome(Routes(activePoll: null, []));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Next game", cut.Find("#next-game-title").TextContent.Trim());
            Assert.Contains("No upcoming poll", cut.Markup);
            Assert.Empty(cut.FindAll("a.home-cta"));
        });
    }

    [Fact]
    public void QuickLeaderboard_ShowsTopFiveByRank_AndMarksOnlyTheCurrentUser()
    {
        var cut = RenderHome(Routes(OpenPoll(), []), currentUserSub: "user-3");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".pd-rank-row");
            string[] expectedOrder = ["Ciara D.", "Jonny M.", "Sam R.", "Aoife B.", "Mark K."];

            Assert.Equal(expectedOrder.Length, rows.Count);
            for (var i = 0; i < expectedOrder.Length; i++)
                Assert.Contains(expectedOrder[i], rows[i].TextContent);

            var youRow = Assert.Single(cut.FindAll(".pd-rank-row.pd-row-you"));
            Assert.Contains("Sam R.", youRow.TextContent);
            Assert.Single(cut.FindAll(".pd-badge-you"));
        });
    }

    [Fact]
    public void ApiFailure_ShowsWarningAlert()
    {
        // No routes registered, so the first API call throws.
        var cut = RenderHome(new RoutingHttpMessageHandler());

        cut.WaitForAssertion(() =>
            Assert.Contains("Unable to load dashboard data", cut.Find("[role='alert']").TextContent));
    }
}
