using System.Net;
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
            ])
        ]);

    private IRenderedComponent<ScorePoll> RenderScorePoll(RoutingHttpMessageHandler? extra = null)
    {
        var handler = extra ?? new RoutingHttpMessageHandler();
        handler.MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll());
        Services.AddSingleton(handler.BuildClient());

        return Render<ScorePoll>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    [Fact]
    public void NoAnswerSelected_SubmitScoresButtonDisabled()
    {
        var cut = RenderScorePoll();

        cut.WaitForAssertion(() =>
        {
            var submit = cut.Find("button.btn-primary");
            Assert.True(submit.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void AllQuestionsAnswered_SubmitScoresButtonEnabled()
    {
        var cut = RenderScorePoll();
        cut.WaitForAssertion(() => cut.Find(".btn-outline-secondary"));

        cut.Find(".btn-outline-secondary").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled")));
    }

    [Fact]
    public void SubmitScores_UserCancelsConfirm_DoesNotScoreOrNavigate()
    {
        var scoreCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/score", _ => { scoreCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderScorePoll(handler);
        cut.WaitForAssertion(() => cut.Find(".btn-outline-secondary"));
        cut.Find(".btn-outline-secondary").Click();

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled")));
        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("confirm"));
        Assert.False(scoreCalled);

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        Assert.DoesNotContain($"results/{PollId}", nav.Uri);
    }

    [Fact]
    public void SubmitScores_UserConfirms_ScoresAndNavigatesToResults()
    {
        var scoreCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/score", _ => { scoreCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderScorePoll(handler);
        cut.WaitForAssertion(() => cut.Find(".btn-outline-secondary"));
        cut.Find(".btn-outline-secondary").Click();

        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled")));
        cut.Find("button.btn-primary").Click();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        cut.WaitForAssertion(() =>
        {
            Assert.True(scoreCalled);
            Assert.Contains($"results/{PollId}", nav.Uri);
        });
    }
}
