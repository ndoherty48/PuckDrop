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

/// <summary>
/// Re-scoring an already-scored poll: the screen has to start from the answers the poll already
/// carries, or an admin correcting one question would silently blank the rest.
/// </summary>
public class RescoreTests : BunitContext
{
    private const string PollId = "poll-1";

    private static PollDetailModel BuildScoredPoll(string? q2Correct = "o4") => new(
        PollId, "2025-26", new DateOnly(2026, 1, 15), "Belfast Giants vs Sheffield Steelers",
        DateTime.UtcNow.AddDays(-1), "Scored", "admin-user", DateTime.UtcNow,
        [
            new QuestionModel("q1", "Who scores first?", 0, "o1",
            [
                new OptionModel("o1", "Home", 0),
                new OptionModel("o2", "Away", 1)
            ]),
            new QuestionModel("q2", "Will it go to overtime?", 1, q2Correct,
            [
                new OptionModel("o3", "Yes", 0),
                new OptionModel("o4", "No", 1)
            ])
        ]);

    private IRenderedComponent<ScorePoll> RenderScorePoll(RoutingHttpMessageHandler handler)
    {
        Services.AddSingleton(handler.BuildClient());
        return Render<ScorePoll>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    private static IElement Button(IRenderedComponent<ScorePoll> cut, string visibleTextStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(visibleTextStart));

    [Fact]
    public void AScoredPoll_PreselectsTheAnswersItAlreadyHas()
    {
        var handler = new RoutingHttpMessageHandler().MapJson(HttpMethod.Get, $"polls/{PollId}", BuildScoredPoll());
        var cut = RenderScorePoll(handler);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 of 2 answers set", cut.Markup);
            Assert.Equal("true", Button(cut, "Home").GetAttribute("aria-pressed"));
            Assert.Equal("true", Button(cut, "No").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void APartlyScoredPoll_ShowsWhichQuestionIsStillMissingAnAnswer()
    {
        // Reachable through the raw API: scoring grades only the questions it is given, yet still
        // marks the poll Scored. Re-scoring is the only way to finish the job.
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildScoredPoll(q2Correct: null));
        var cut = RenderScorePoll(handler);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 of 2 answers set", cut.Markup);
            Assert.Contains("Not set", cut.Markup);
        });
    }

    [Fact]
    public void AScoredPoll_UsesRescoreWording()
    {
        var handler = new RoutingHttpMessageHandler().MapJson(HttpMethod.Get, $"polls/{PollId}", BuildScoredPoll());
        var cut = RenderScorePoll(handler);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Ready to re-score?", cut.Markup);
            Assert.NotNull(Button(cut, "Update scores"));
        });
    }

    [Fact]
    public void Rescoring_SendsTheCorrectedAnswers_IncludingTheOnesLeftAlone()
    {
        // The whole point of the prefill: changing one question must not blank the other.
        ScorePollRequest? sent = null;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildScoredPoll())
            .Map(HttpMethod.Post, $"polls/{PollId}/score", request =>
            {
                sent = request.Content!.ReadFromJsonAsync<ScorePollRequest>().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var cut = RenderScorePoll(handler);

        cut.WaitForAssertion(() => Button(cut, "Away"));
        Button(cut, "Away").Click();            // correct q1 from Home to Away
        Button(cut, "Update scores").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal("o2", sent!.Answers.Single(a => a.QuestionId == "q1").CorrectOptionId);
            Assert.Equal("o4", sent.Answers.Single(a => a.QuestionId == "q2").CorrectOptionId);
        });
    }

    [Fact]
    public void Rescoring_WarnsThatTheLeaderboardIsRecalculated()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildScoredPoll())
            .Map(HttpMethod.Post, $"polls/{PollId}/score", _ => new HttpResponseMessage(HttpStatusCode.OK));
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var cut = RenderScorePoll(handler);

        cut.WaitForAssertion(() => Button(cut, "Update scores"));
        Button(cut, "Update scores").Click();

        var confirm = JSInterop.Invocations["confirm"].Single();
        Assert.Contains("recalculates the leaderboard", (string)confirm.Arguments[0]!);
    }
}
