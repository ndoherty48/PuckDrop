using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

public class HistoryTests : BunitContext
{
    private static readonly SeasonModel Season =
        new("2026-27", "EIHL 2026–27", new DateOnly(2026, 8, 29), new DateOnly(2027, 4, 11));

    private static PollModel BuildPoll(string pollId, DateOnly gameDate, string status) => new(
        pollId, Season.SeasonId, gameDate, $"Poll {pollId}", gameDate.ToDateTime(new TimeOnly(19, 0)),
        status, "admin", DateTime.UtcNow);

    private IRenderedComponent<History> RenderHistory(List<PollModel> polls)
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "seasons/current", Season)
            .MapJson(HttpMethod.Get, $"polls?seasonId={Season.SeasonId}", polls);
        Services.AddSingleton(handler.BuildClient());

        return Render<History>();
    }

    [Fact]
    public void ShowsOnlyClosedAndScoredPolls_NewestFirst()
    {
        var cut = RenderHistory(
        [
            BuildPoll("open", new DateOnly(2026, 9, 19), "Open"),
            BuildPoll("draft", new DateOnly(2026, 9, 23), "Draft"),
            BuildPoll("scored-older", new DateOnly(2026, 9, 5), "Scored"),
            BuildPoll("closed", new DateOnly(2026, 9, 13), "Closed"),
            BuildPoll("scored", new DateOnly(2026, 9, 12), "Scored")
        ]);

        cut.WaitForAssertion(() =>
            Assert.Equal(
                new[] { "Poll closed", "Poll scored", "Poll scored-older" },
                cut.FindAll(".history-title").Select(t => t.TextContent.Trim())));
    }

    [Fact]
    public void OnlyScoredPollsLinkToResults_WithThePollNamedInTheLink()
    {
        var cut = RenderHistory(
        [
            BuildPoll("closed", new DateOnly(2026, 9, 13), "Closed"),
            BuildPoll("scored", new DateOnly(2026, 9, 12), "Scored")
        ]);

        cut.WaitForAssertion(() =>
        {
            var link = Assert.Single(cut.FindAll("a[href^='results/']"));
            Assert.Equal("results/scored", link.GetAttribute("href"));
            Assert.Contains("View results for Poll scored", link.TextContent);

            var closedRow = cut.FindAll(".history-row")[0];
            Assert.Contains("Closed", closedRow.TextContent);
            Assert.Contains("Results after scoring", closedRow.TextContent);
        });
    }

    [Fact]
    public void NoPastPolls_ShowsEmptyMessage()
    {
        var cut = RenderHistory([BuildPoll("open", new DateOnly(2026, 9, 19), "Open")]);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No past polls found.", cut.Markup);
            Assert.Empty(cut.FindAll(".history-row"));
        });
    }

    [Fact]
    public void NoCurrentSeason_ShowsAnError()
    {
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Get, "seasons/current", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Services.AddSingleton(handler.BuildClient());

        var cut = Render<History>();

        cut.WaitForAssertion(() => Assert.Contains("No current season found.", cut.Find("[role=alert]").TextContent));
    }
}
