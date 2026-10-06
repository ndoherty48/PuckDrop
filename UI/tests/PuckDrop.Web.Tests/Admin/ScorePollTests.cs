using System.Net;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages.Admin;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests.Admin;

public class ScorePollTests : BunitContext
{
    private const string PollId = "poll-1";

    private static PollDetailModel BuildPoll(string status = "Closed") => new(
        PollId, "2025-26", new DateOnly(2026, 1, 15), "Belfast Giants vs Sheffield Steelers",
        DateTime.UtcNow.AddDays(-1), status, "admin-user", DateTime.UtcNow,
        [
            new QuestionModel("q1", "Who scores first?", 0, null,
            [
                new OptionModel("o1", "Home", 0),
                new OptionModel("o2", "Away", 1)
            ]),
            new QuestionModel("q2", "Will it go to overtime?", 1, null,
            [
                new OptionModel("o3", "Yes", 0),
                new OptionModel("o4", "No", 1)
            ])
        ]);

    private ConfirmDialogStub Confirm { get; set; } = null!;

    private IRenderedComponent<ScorePoll> RenderScorePoll(
        RoutingHttpMessageHandler? extra = null, bool confirmResult = true, string status = "Closed")
    {
        var handler = extra ?? new RoutingHttpMessageHandler();
        handler.MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll(status));
        Services.AddSingleton(handler.BuildClient());
        Confirm = ConfirmDialogStub.Register(this, confirmResult);

        return Render<ScorePoll>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    private static IElement Button(IRenderedComponent<ScorePoll> cut, string visibleTextStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(visibleTextStart));

    private static void AnswerBothQuestions(IRenderedComponent<ScorePoll> cut)
    {
        cut.WaitForAssertion(() => Button(cut, "Home"));
        Button(cut, "Home").Click();
        Button(cut, "No").Click();
    }

    [Fact]
    public void ChoosingAnAnswer_PressesOnlyThatOption_AndCountsIt()
    {
        var cut = RenderScorePoll();
        cut.WaitForAssertion(() => Button(cut, "Home"));
        Assert.Equal("false", Button(cut, "Home").GetAttribute("aria-pressed"));

        Button(cut, "Home").Click();

        Assert.Equal("true", Button(cut, "Home").GetAttribute("aria-pressed"));
        Assert.Equal("false", Button(cut, "Away").GetAttribute("aria-pressed"));
        Assert.Contains("1 of 2 answers set", cut.Markup);

        Button(cut, "Away").Click();

        Assert.Equal("false", Button(cut, "Home").GetAttribute("aria-pressed"));
        Assert.Equal("true", Button(cut, "Away").GetAttribute("aria-pressed"));
        Assert.Contains("1 of 2 answers set", cut.Markup);
    }

    [Fact]
    public void SubmitWithAQuestionUnset_FlagsAndFocusesIt_WithoutConfirming()
    {
        var cut = RenderScorePoll();
        cut.WaitForAssertion(() => Button(cut, "Home"));
        Button(cut, "Home").Click();

        Button(cut, "Submit scores").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Pick the correct answer for question 2 before submitting.", cut.Find("[role=alert]").TextContent);
            Assert.Contains("Pick the correct answer", cut.Find("#q-q2 .pd-q-head").TextContent);
            Assert.DoesNotContain("Pick the correct answer", cut.Find("#q-q1 .pd-q-head").TextContent);
        });
        JSInterop.VerifyFocusAsyncInvoke();
        Assert.Null(Confirm.LastRequest);
    }

    [Fact]
    public void SubmitScores_UserCancelsConfirm_DoesNotScoreOrNavigate()
    {
        var scoreCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/score", _ => { scoreCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderScorePoll(handler, confirmResult: false);
        AnswerBothQuestions(cut);

        Button(cut, "Submit scores").Click();

        cut.WaitForAssertion(() => Assert.NotNull(Confirm.LastRequest));
        Assert.False(scoreCalled);

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain($"results/{PollId}", nav.Uri);
    }

    [Fact]
    public void SubmitScores_UserConfirms_SendsTheCorrectAnswersAndNavigatesToResults()
    {
        string? submittedBody = null;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/score", request =>
            {
                submittedBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        var cut = RenderScorePoll(handler);
        AnswerBothQuestions(cut);

        Button(cut, "Submit scores").Click();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        cut.WaitForAssertion(() => Assert.Contains($"results/{PollId}", nav.Uri));
        Assert.NotNull(submittedBody);
        Assert.Contains("\"o1\"", submittedBody);
        Assert.Contains("\"o4\"", submittedBody);
    }

    [Fact]
    public void ScoringFails_ShowsAnError_AndStaysOnThePage()
    {
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/score", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = RenderScorePoll(handler);
        AnswerBothQuestions(cut);

        Button(cut, "Submit scores").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Failed to submit scores. Please try again.", cut.Find("[role=alert]").TextContent));
        Assert.DoesNotContain($"results/{PollId}", Services.GetRequiredService<BunitNavigationManager>().Uri);
    }

    [Fact]
    public void PollNotFound_SaysSo()
    {
        // Registered first, so it wins over RenderScorePoll's own GET route.
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Get, $"polls/{PollId}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderScorePoll(handler);

        cut.WaitForAssertion(() => Assert.Equal("Poll not found", cut.Find("h1").TextContent.Trim()));
    }

    private static RoutingHttpMessageHandler WithParticipation(int picked, int players, params string[] stillToPick) =>
        new RoutingHttpMessageHandler().MapJson(HttpMethod.Get, $"polls/{PollId}/participation",
            new PollParticipationDetailModel(PollId, picked, players,
                stillToPick.Select(name => new PlayerModel($"u-{name}", name)).ToList()));

    [Fact]
    public void SomeoneDidntPick_WarnsByNameWithoutBlockingScoring()
    {
        var cut = RenderScorePoll(WithParticipation(8, 9, "Mark"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("8 of 9 players picked", cut.Find(".pd-meta-row .pd-badge .pd-sr-only").TextContent);
            var note = cut.Find(".score-pickers");
            Assert.Contains("pd-alert-warning", note.ClassList);
            Assert.Equal("8 of 9 players picked. Mark didn't pick and will get 0 points.", note.TextContent.Trim());
        });
        Assert.False(Button(cut, "Submit").HasAttribute("disabled"));
    }

    [Fact]
    public void EveryonePicked_SaysSo()
    {
        var cut = RenderScorePoll(WithParticipation(9, 9));

        cut.WaitForAssertion(() =>
        {
            var note = cut.Find(".score-pickers");
            Assert.Contains("pd-alert-success", note.ClassList);
            Assert.Equal("All 9 players picked.", note.TextContent.Trim());
        });
    }

    [Fact]
    public void Rescore_JustStatesTheCount()
    {
        var cut = RenderScorePoll(WithParticipation(8, 9, "Mark"), status: "Scored");

        cut.WaitForAssertion(() =>
        {
            var note = cut.Find(".score-pickers");
            Assert.DoesNotContain("pd-alert", note.ClassList);
            Assert.Equal("8 of 9 players picked.", note.TextContent.Trim());
        });
    }

    [Fact]
    public void ParticipationFailsToLoad_ScoringStillWorks_WithoutTheNote()
    {
        // No participation route is registered, so that call throws.
        var cut = RenderScorePoll();

        cut.WaitForAssertion(() => Button(cut, "Home"));
        Assert.Empty(cut.FindAll(".score-pickers"));
        Assert.Single(cut.FindAll(".pd-meta-row .pd-badge"));
    }
}
