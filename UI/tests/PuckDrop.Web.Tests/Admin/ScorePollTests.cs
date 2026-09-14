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

    private static PollDetailModel BuildPoll() => new(
        PollId, "2025-26", new DateOnly(2026, 1, 15), "Belfast Giants vs Sheffield Steelers",
        DateTime.UtcNow.AddDays(-1), "Closed", "admin-user", DateTime.UtcNow,
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

    private IRenderedComponent<ScorePoll> RenderScorePoll(RoutingHttpMessageHandler? extra = null)
    {
        var handler = extra ?? new RoutingHttpMessageHandler();
        handler.MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll());
        Services.AddSingleton(handler.BuildClient());

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
        JSInterop.VerifyNotInvoke("confirm");
    }

    [Fact]
    public void SubmitScores_UserCancelsConfirm_DoesNotScoreOrNavigate()
    {
        var scoreCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/score", _ => { scoreCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderScorePoll(handler);
        AnswerBothQuestions(cut);

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        Button(cut, "Submit scores").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("confirm"));
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

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
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

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
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
}
