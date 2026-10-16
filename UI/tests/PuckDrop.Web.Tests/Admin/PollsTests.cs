using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages.Admin;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests.Admin;

public class PollsTests : BunitContext
{
    private static SeasonModel BuildSeason() => new("2025-26", "2025/26 Season", new DateOnly(2025, 8, 1), new DateOnly(2026, 4, 30));

    private static PollModel BuildPoll(string pollId = "poll-1", string status = "Draft", DateOnly? gameDate = null) => new(
        pollId, "2025-26", gameDate ?? new DateOnly(2026, 1, 15), $"Poll {pollId}",
        DateTime.UtcNow.AddDays(1), status, "admin-user", DateTime.UtcNow);

    private ConfirmDialogStub Confirm { get; set; } = null!;

    private IRenderedComponent<Polls> RenderWithHandler(RoutingHttpMessageHandler handler, bool confirmResult = true)
    {
        handler.MapJson(HttpMethod.Get, "seasons/current", BuildSeason());
        Services.AddSingleton(handler.BuildClient());
        Confirm = ConfirmDialogStub.Register(this, confirmResult);
        return Render<Polls>();
    }

    // The desktop table's control; the phone card list renders the same controls, hidden by CSS.
    private static IElement TableControl(IRenderedComponent<Polls> cut, string visibleText) =>
        cut.FindAll(".polls-table a, .polls-table button").First(c => c.TextContent.TrimStart().StartsWith(visibleText));

    [Fact]
    public void PublishButton_UserCancelsConfirm_DoesNotCallPublish()
    {
        var publishCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildPoll() })
            .Map(HttpMethod.Post, "polls/poll-1/publish", _ => { publishCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderWithHandler(handler, confirmResult: false);

        cut.WaitForAssertion(() => TableControl(cut, "Publish"));
        TableControl(cut, "Publish").Click();

        cut.WaitForAssertion(() => Assert.NotNull(Confirm.LastRequest));
        Assert.False(publishCalled);
    }

    [Fact]
    public void PublishButton_UserConfirms_CallsPublishAndAnnouncesIt()
    {
        var publishCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildPoll() })
            .Map(HttpMethod.Post, "polls/poll-1/publish", _ =>
            {
                publishCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(BuildPoll() with { Status = "Open" })
                };
            });
        var cut = RenderWithHandler(handler);

        cut.WaitForAssertion(() => TableControl(cut, "Publish"));
        TableControl(cut, "Publish").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(publishCalled);
            Assert.Contains("“Poll poll-1” is now open for picks.", cut.Find("[role=status]").TextContent);
        });
    }

    [Fact]
    public void CloseButton_UserCancelsConfirm_DoesNotCallClose()
    {
        var closeCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildPoll(status: "Open") })
            .Map(HttpMethod.Post, "polls/poll-1/close", _ => { closeCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderWithHandler(handler, confirmResult: false);

        cut.WaitForAssertion(() => TableControl(cut, "Close voting"));
        TableControl(cut, "Close voting").Click();

        cut.WaitForAssertion(() => Assert.NotNull(Confirm.LastRequest));
        Assert.False(closeCalled);
    }

    // The rows of the section headed by the given heading, as row-header text, top to bottom
    private static string[] SectionRows(IRenderedComponent<Polls> cut, string headingId) =>
        cut.Find($"section[aria-labelledby={headingId}]").QuerySelectorAll(".polls-table tbody th")
            .Select(th => th.TextContent.Trim()).ToArray();

    [Fact]
    public void Polls_SplitIntoUpcomingOldestFirst_AndScoredNewestFirst_WithActionsForEachStatus_NamingThePoll()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel>
            {
                BuildPoll("scored", "Scored", new DateOnly(2026, 1, 10)),
                BuildPoll("draft", "Draft", new DateOnly(2026, 1, 24)),
                BuildPoll("closed", "Closed", new DateOnly(2026, 1, 12)),
                BuildPoll("open", "Open", new DateOnly(2026, 1, 17)),
                BuildPoll("scored-newer", "Scored", new DateOnly(2026, 1, 11))
            });
        var cut = RenderWithHandler(handler);

        // Visible label of each control in a row, checking each also names the poll.
        static string[] Controls(IElement row, string pollTitle) =>
            row.QuerySelectorAll("a, button")
                .Select(control =>
                {
                    Assert.Contains(pollTitle, control.TextContent);
                    return control.TextContent.Replace(pollTitle, "").Replace(" for", "").Trim();
                })
                .ToArray();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "Upcoming", "Scored" }, cut.FindAll("h2").Select(h => h.TextContent.Trim()));

            // A Closed poll's game has been played, so it leads Upcoming, waiting to be scored
            Assert.Equal(new[] { "Poll closed", "Poll open", "Poll draft" }, SectionRows(cut, "polls-upcoming"));
            Assert.Equal(new[] { "Poll scored-newer", "Poll scored" }, SectionRows(cut, "polls-scored"));
            Assert.Equal(
                new[] { "Upcoming polls, oldest game first", "Scored polls, newest first" },
                cut.FindAll(".polls-table caption").Select(c => c.TextContent));
            Assert.Equal(
                new[] { "Upcoming polls", "Scored polls" },
                cut.FindAll(".polls-cards").Select(list => list.GetAttribute("aria-label")));

            var rows = cut.FindAll(".polls-table tbody tr");
            // Re-score is offered on a Scored poll so a wrong correct option can be fixed, or a
            // partly scored poll finished.
            Assert.Equal(new[] { "Score" }, Controls(rows[0], "Poll closed"));
            Assert.Equal(new[] { "Edit", "Close voting" }, Controls(rows[1], "Poll open"));
            Assert.Equal(new[] { "Edit", "Publish" }, Controls(rows[2], "Poll draft"));
            Assert.Equal(new[] { "View results", "Re-score" }, Controls(rows[4], "Poll scored"));

            Assert.Equal("admin/polls/closed/score", rows[0].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("admin/polls/draft/edit", rows[2].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("results/scored", rows[4].QuerySelector("a")!.GetAttribute("href"));

            Assert.Equal(5, cut.FindAll(".polls-cards > li").Count);
            Assert.Empty(cut.FindAll(".polls-more"));
        });
    }

    [Fact]
    public void PollsOnTheSameDate_KeepTheOrderTheyWereCreatedIn_NewestFirstOnceScored()
    {
        var date = new DateOnly(2026, 1, 10);
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel>
            {
                BuildPoll("open-later", "Open", date) with { CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
                BuildPoll("open-earlier", "Open", date) with { CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                BuildPoll("scored-earlier", "Scored", date) with { CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                BuildPoll("scored-later", "Scored", date) with { CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) }
            });
        var cut = RenderWithHandler(handler);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "Poll open-earlier", "Poll open-later" }, SectionRows(cut, "polls-upcoming"));
            Assert.Equal(new[] { "Poll scored-later", "Poll scored-earlier" }, SectionRows(cut, "polls-scored"));
        });
    }

    [Fact]
    public void ManyScoredPolls_ShowTheNewestThree_UntilExpanded()
    {
        var polls = Enumerable.Range(1, 5)
            .Select(day => BuildPoll($"scored-{day}", "Scored", new DateOnly(2026, 1, day)))
            .ToList();
        var cut = RenderWithHandler(new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", polls));

        cut.WaitForAssertion(() =>
            Assert.Equal(new[] { "Poll scored-5", "Poll scored-4", "Poll scored-3" }, SectionRows(cut, "polls-scored")));
        Assert.Equal(3, cut.FindAll(".polls-cards > li").Count);
        Assert.Contains("5 polls · newest first", cut.Find(".polls-section-meta").TextContent);

        var toggle = cut.Find(".polls-more");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Show 2 earlier polls", toggle.TextContent.Trim());

        toggle.Click();

        Assert.Equal(5, SectionRows(cut, "polls-scored").Length);
        Assert.Equal("true", cut.Find(".polls-more").GetAttribute("aria-expanded"));
        Assert.Equal("Show fewer polls", cut.Find(".polls-more").TextContent.Trim());

        cut.Find(".polls-more").Click();

        Assert.Equal(3, SectionRows(cut, "polls-scored").Length);
    }

    [Fact]
    public void NothingUpcoming_SaysAllCaughtUp_AndOffersToImportFixtures()
    {
        var cut = RenderWithHandler(new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildPoll(status: "Scored") }));

        cut.WaitForAssertion(() =>
        {
            var upcoming = cut.Find("section[aria-labelledby=polls-upcoming]");
            Assert.Contains("All caught up", upcoming.TextContent);
            Assert.Empty(upcoming.QuerySelectorAll("table"));
            Assert.Equal("admin/polls/import", upcoming.QuerySelector("a")!.GetAttribute("href"));
            Assert.Single(SectionRows(cut, "polls-scored"));
        });
    }

    [Fact]
    public void NothingScoredYet_LeavesOutTheScoredSection()
    {
        var cut = RenderWithHandler(new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildPoll(status: "Open") }));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(SectionRows(cut, "polls-upcoming"));
            Assert.Equal(new[] { "Upcoming" }, cut.FindAll("h2").Select(h => h.TextContent.Trim()));
            Assert.Empty(cut.FindAll("section[aria-labelledby=polls-scored]"));
        });
    }

    [Fact]
    public void LoadFailure_ShowsAFriendlyError()
    {
        // Only the season route is registered, so loading the polls throws.
        var cut = RenderWithHandler(new RoutingHttpMessageHandler());

        cut.WaitForAssertion(() =>
            Assert.Contains("Unable to load polls. Please try again later.", cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void PickCounts_ShowPickedOutOfSeasonPlayers_ColouredByWhetherEveryonesIn()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel>
            {
                BuildPoll("scored", "Scored", new DateOnly(2026, 1, 1)),
                BuildPoll("closed", "Closed", new DateOnly(2026, 1, 2)),
                BuildPoll("open", "Open", new DateOnly(2026, 1, 3)),
                BuildPoll("draft", "Draft", new DateOnly(2026, 1, 4))
            })
            .MapJson(HttpMethod.Get, "polls/participation?seasonId=2025-26", new SeasonParticipationModel(9,
            [
                new("scored", 8),
                new("closed", 9),
                new("open", 5)
            ]));
        var cut = RenderWithHandler(handler);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Picks", cut.Find(".polls-table thead").TextContent);
            Assert.Equal(4, cut.FindAll(".polls-table tbody tr").Count);
            // Keyed by poll, since Scored polls sit in their own section after the upcoming ones
            var picks = cut.FindAll(".polls-table tbody tr")
                .ToDictionary(row => row.QuerySelector("th")!.TextContent.Trim(), row => row.QuerySelectorAll("td")[2]);

            Assert.Equal("8 of 9 players picked", picks["Poll scored"].QuerySelector(".pd-sr-only")!.TextContent);
            Assert.Contains("pd-badge-draft", picks["Poll scored"].QuerySelector(".pd-badge")!.ClassList);
            Assert.Contains("pd-badge-open", picks["Poll closed"].QuerySelector(".pd-badge")!.ClassList);
            Assert.Equal("5 / 9", picks["Poll open"].QuerySelector("[aria-hidden=true]:not(svg)")!.TextContent);
            Assert.Contains("pd-badge-closed", picks["Poll open"].QuerySelector(".pd-badge")!.ClassList);
            Assert.Null(picks["Poll draft"].QuerySelector(".pd-badge"));
            Assert.Equal("No picks until published", picks["Poll draft"].QuerySelector(".pd-sr-only")!.TextContent);
        });
    }

    [Fact]
    public void PickCountsFailToLoad_PollsStillShow_WithoutThePicksColumn()
    {
        // No participation route is registered, so that call throws.
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel> { BuildPoll(status: "Open") });
        var cut = RenderWithHandler(handler);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".polls-table tbody tr"));
            Assert.DoesNotContain("Picks", cut.Find(".polls-table thead").TextContent);
            Assert.Empty(cut.FindAll("[role=alert]"));
        });
    }
}
