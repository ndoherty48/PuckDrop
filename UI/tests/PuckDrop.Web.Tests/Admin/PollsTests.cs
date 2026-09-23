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

    [Fact]
    public void Rows_AreNewestFirst_WithActionsForEachStatus_NamingThePoll()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "polls?seasonId=2025-26", new List<PollModel>
            {
                BuildPoll("scored", "Scored", new DateOnly(2026, 1, 10)),
                BuildPoll("draft", "Draft", new DateOnly(2026, 1, 24)),
                BuildPoll("closed", "Closed", new DateOnly(2026, 1, 12)),
                BuildPoll("open", "Open", new DateOnly(2026, 1, 17))
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
            var rows = cut.FindAll(".polls-table tbody tr");
            Assert.Equal(
                new[] { "Poll draft", "Poll open", "Poll closed", "Poll scored" },
                rows.Select(row => row.QuerySelector("th")!.TextContent.Trim()));

            Assert.Equal(new[] { "Edit", "Publish" }, Controls(rows[0], "Poll draft"));
            Assert.Equal(new[] { "Edit", "Close voting" }, Controls(rows[1], "Poll open"));
            Assert.Equal(new[] { "Score" }, Controls(rows[2], "Poll closed"));
            // Re-score is offered on a Scored poll so a wrong correct option can be fixed, or a
            // partly scored poll finished.
            Assert.Equal(new[] { "View results", "Re-score" }, Controls(rows[3], "Poll scored"));

            Assert.Equal("admin/polls/draft/edit", rows[0].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("admin/polls/closed/score", rows[2].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("results/scored", rows[3].QuerySelector("a")!.GetAttribute("href"));

            Assert.Equal(4, cut.FindAll(".polls-cards > li").Count);
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
}
