using System.Net;
using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

public class ResultsTests : BunitContext
{
    private const string PollId = "poll-1";

    private static readonly List<QuestionModel> Questions =
    [
        new QuestionModel("q1", "Who wins?", 0, "o1",
        [
            new OptionModel("o1", "Cardiff Devils", 0),
            new OptionModel("o2", "Belfast Giants", 1)
        ]),
        new QuestionModel("q2", "Will it go to overtime?", 1, "o4",
        [
            new OptionModel("o3", "Yes", 0),
            new OptionModel("o4", "No", 1)
        ])
    ];

    private static PollDetailModel BuildPoll() => new(
        PollId, "2026-27", new DateOnly(2026, 9, 12), "Cardiff Devils vs Belfast Giants",
        new DateTime(2026, 9, 12, 18, 45, 0, DateTimeKind.Utc), "Scored", "admin", DateTime.UtcNow, Questions);

    // Deliberately not in points order - the page ranks by points.
    private static PollResultsModel BuildResults() => new(PollId, "Scored", Questions,
    [
        new UserResultModel("u2", "Friend",
        [
            new UserAnswerModel("q1", "o2", DateTime.UtcNow, false),
            new UserAnswerModel("q2", "o4", DateTime.UtcNow, true)
        ], 1),
        new UserResultModel("u1", "Nathan",
        [
            new UserAnswerModel("q1", "o1", DateTime.UtcNow, true),
            new UserAnswerModel("q2", "o4", DateTime.UtcNow, true)
        ], 2),
        new UserResultModel("u3", "Late Joiner", [], 0)
    ]);

    private IRenderedComponent<Results> RenderResults(RoutingHttpMessageHandler handler, string currentUserSub = "u2")
    {
        Services.AddSingleton(handler.BuildClient());
        Services.AddSingleton(new ConfirmDialogService());

        var authContext = AddAuthorization();
        authContext.SetAuthorized("Friend");
        authContext.SetClaims(new Claim("sub", currentUserSub));

        return Render<Results>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    private static RoutingHttpMessageHandler Routes() => new RoutingHttpMessageHandler()
        .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll())
        .MapJson(HttpMethod.Get, $"polls/{PollId}/results", BuildResults());

    [Fact]
    public void Table_RanksPlayersByPoints_AndMarksEachPickCorrectOrWrong()
    {
        var cut = RenderResults(Routes());

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".results-table tbody tr");
            Assert.Equal(
                new[] { "Nathan", "Friend", "Late Joiner" },
                rows.Select(row => row.QuerySelector("th")!.TextContent.Replace("You", "").Trim()));

            var friendCells = rows[1].QuerySelectorAll("td");
            Assert.Contains("pd-cell-wrong", friendCells[0].ClassName);
            Assert.Contains("Belfast Giants, wrong", friendCells[0].TextContent);
            Assert.Contains("pd-cell-correct", friendCells[1].ClassName);
            Assert.Contains("No, correct", friendCells[1].TextContent);
            Assert.Equal("1", friendCells[2].TextContent.Trim());

            Assert.Contains("No pick", rows[2].QuerySelectorAll("td")[0].TextContent);
        });
    }

    [Fact]
    public void Header_ShowsTitleCorrectAnswersAndYourScore()
    {
        var cut = RenderResults(Routes());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Cardiff Devils vs Belfast Giants", cut.Find("h1").TextContent.Trim());
            Assert.Contains("You scored 1 of 2", cut.Markup);
            Assert.Contains("Answer: Cardiff Devils", cut.Find(".results-table thead").TextContent);
            Assert.Contains("pd-row-you", cut.FindAll(".results-table tbody tr")[1].ClassName);
        });
    }

    [Fact]
    public void QuestionSections_FirstOpenByDefault_AndToggleOpen()
    {
        var cut = RenderResults(Routes());

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".results-mobile .pd-disclosure").Count));

        var buttons = cut.FindAll(".results-mobile .pd-disclosure");
        Assert.Equal("true", buttons[0].GetAttribute("aria-expanded"));
        Assert.Contains("1 of 2 got it right", buttons[0].TextContent);
        Assert.Equal("false", buttons[1].GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("#results-q-q2"));

        buttons[1].Click();

        Assert.Equal("true", cut.FindAll(".results-mobile .pd-disclosure")[1].GetAttribute("aria-expanded"));
        var panel = cut.Find("#results-q-q2");
        Assert.Contains("Late Joiner", panel.TextContent);
        Assert.Contains("No pick", panel.TextContent);
    }

    [Fact]
    public void ResultsNotAvailable_ShowsMessage()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll())
            .Map(HttpMethod.Get, $"polls/{PollId}/results", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderResults(handler);

        cut.WaitForAssertion(() => Assert.Contains("Results aren't available for this poll yet.", cut.Markup));
    }

    [Fact]
    public void LoadFailure_ShowsAFriendlyError()
    {
        // No routes registered, so the first API call throws.
        var cut = RenderResults(new RoutingHttpMessageHandler());

        cut.WaitForAssertion(() =>
            Assert.Contains("Unable to load results. Please try again later.", cut.Find("[role=alert]").TextContent));
    }
}
