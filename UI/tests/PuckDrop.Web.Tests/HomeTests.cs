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

    private static PollModel SecondOpenPoll() => new(
        "poll-2", Season.SeasonId, new DateOnly(2026, 9, 20), "Sheffield Steelers vs Glasgow Clan",
        DateTime.UtcNow.AddDays(6).AddHours(1), "Open", "admin", DateTime.UtcNow);

    // Still Open, but its deadline has passed - waiting on an admin to close it
    private static PollModel PastDeadlinePoll() => new(
        "poll-0", Season.SeasonId, new DateOnly(2026, 9, 13), "Cardiff Devils vs Nottingham Panthers",
        DateTime.UtcNow.AddHours(-3), "Open", "admin", DateTime.UtcNow);

    private static readonly List<UserAnswerModel> SomePicks = [new UserAnswerModel("q-1", "o-1", DateTime.UtcNow, null)];

    private static LeaderboardModel BuildLeaderboard() => new(Season.SeasonId,
    [
        LeaderboardEntries.Entry("user-6", "Rory N.", 6, 12, 6),
        LeaderboardEntries.Entry("user-1", "Ciara D.", 10, 12, 1),
        LeaderboardEntries.Entry("user-3", "Sam R.", 8, 12, 3),
        LeaderboardEntries.Entry("user-2", "Jonny M.", 9, 12, 2),
        LeaderboardEntries.Entry("user-5", "Mark K.", 7, 12, 5),
        LeaderboardEntries.Entry("user-4", "Aoife B.", 7, 9, 4)
    ]);

    private static RoutingHttpMessageHandler Routes(PollModel? activePoll, List<UserAnswerModel> existingAnswers) =>
        Routes(activePoll is null ? [] : [(activePoll, existingAnswers)]);

    // Active polls in the order the API returns them: soonest deadline first
    private static RoutingHttpMessageHandler Routes(List<(PollModel Poll, List<UserAnswerModel> Answers)> activePolls)
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "seasons/current", Season)
            .MapJson(HttpMethod.Get, $"polls/active?seasonId={Season.SeasonId}", activePolls.Select(p => p.Poll).ToList())
            .MapJson(HttpMethod.Get, $"leaderboard?seasonId={Season.SeasonId}", BuildLeaderboard());

        foreach (var (poll, answers) in activePolls)
            handler.MapJson(HttpMethod.Get, $"polls/{poll.PollId}/answers", answers);

        return handler;
    }

    private IRenderedComponent<Home> RenderHome(RoutingHttpMessageHandler handler, string currentUserSub = "user-3")
    {
        Services.AddSingleton(handler.BuildClient());
        Services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));

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
    public void OneOpenPoll_HasNoAlsoOpenList()
    {
        var cut = RenderHome(Routes(OpenPoll(), []));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Belfast Giants vs Sheffield Steelers", cut.Find("#next-game-title").TextContent.Trim());
            Assert.DoesNotContain("Also open", cut.Markup);
        });
    }

    [Fact]
    public void TwoOpenPolls_NeitherPicked_HeroIsSoonest_OtherIsListedAsNotInYet()
    {
        var cut = RenderHome(Routes([(OpenPoll(), []), (SecondOpenPoll(), [])]));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Belfast Giants vs Sheffield Steelers", cut.Find("#next-game-title").TextContent.Trim());

            var row = Assert.Single(cut.FindAll("a.home-also-row"));
            Assert.Equal("poll/poll-2", row.GetAttribute("href"));
            Assert.Contains("Sheffield Steelers vs Glasgow Clan", row.TextContent);
            Assert.Contains("Closes in 6 days", row.TextContent);
            Assert.Contains("Not in yet", row.TextContent);
        });
    }

    [Fact]
    public void SoonestAlreadyPicked_HeroMovesToThePollStillNeedingPicks()
    {
        var cut = RenderHome(Routes([(OpenPoll(), SomePicks), (SecondOpenPoll(), [])]));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Sheffield Steelers vs Glasgow Clan", cut.Find("#next-game-title").TextContent.Trim());
            Assert.Equal("Make your picks", cut.Find("a.home-cta").TextContent.Trim());

            var row = Assert.Single(cut.FindAll("a.home-also-row"));
            Assert.Equal("poll/poll-1", row.GetAttribute("href"));
            Assert.Contains("Picks in", row.TextContent);
        });
    }

    [Fact]
    public void AllPicked_HeroIsSoonest_WithUpdateYourPicks()
    {
        var cut = RenderHome(Routes([(OpenPoll(), SomePicks), (SecondOpenPoll(), SomePicks)]));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Belfast Giants vs Sheffield Steelers", cut.Find("#next-game-title").TextContent.Trim());
            Assert.Equal("Update your picks", cut.Find("a.home-cta").TextContent.Trim());
            Assert.Contains("Picks in", Assert.Single(cut.FindAll("a.home-also-row")).TextContent);
        });
    }

    [Fact]
    public void AlsoOpen_ListsStillToPickFirst_ThenPicked_ThenPastDeadline()
    {
        var thirdPoll = SecondOpenPoll() with
        {
            PollId = "poll-3", Title = "Glasgow Clan vs Fife Flyers", Deadline = DateTime.UtcNow.AddDays(9)
        };

        // poll-1 is picked and closes soonest, so it would lead the list in plain deadline order
        var cut = RenderHome(Routes(
        [
            (PastDeadlinePoll(), []), (OpenPoll(), SomePicks), (SecondOpenPoll(), []), (thirdPoll, [])
        ]));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Sheffield Steelers vs Glasgow Clan", cut.Find("#next-game-title").TextContent.Trim());

            var hrefs = cut.FindAll("a.home-also-row").Select(r => r.GetAttribute("href")).ToArray();
            Assert.Equal(["poll/poll-3", "poll/poll-1", "poll/poll-0"], hrefs);
        });
    }

    [Fact]
    public void OpenPollPastItsDeadline_NeverLeads_AndIsListedAsPicksClosed()
    {
        var cut = RenderHome(Routes([(PastDeadlinePoll(), []), (OpenPoll(), SomePicks)]));

        cut.WaitForAssertion(() =>
        {
            // Even though the past-deadline poll is unpicked, nothing can be done about it now
            Assert.Equal("Belfast Giants vs Sheffield Steelers", cut.Find("#next-game-title").TextContent.Trim());

            var row = Assert.Single(cut.FindAll("a.home-also-row"));
            Assert.Equal("poll/poll-0", row.GetAttribute("href"));
            Assert.Contains("Picks closed", row.TextContent);
            Assert.Contains("Awaiting results", row.TextContent);
        });
    }

    [Fact]
    public void OnlyOpenPollIsPastItsDeadline_StillShownInTheHero()
    {
        var cut = RenderHome(Routes(PastDeadlinePoll(), []));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Cardiff Devils vs Nottingham Panthers", cut.Find("#next-game-title").TextContent.Trim());
            Assert.Empty(cut.FindAll("a.home-also-row"));
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
