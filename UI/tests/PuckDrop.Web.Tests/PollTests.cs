using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

public class PollTests : BunitContext
{
    private const string PollId = "poll-1";

    private static PollDetailModel BuildPoll(DateTime deadline) => new(
        PollId, "2025-26", new DateOnly(2026, 1, 15), "Belfast Giants vs Sheffield Steelers", deadline,
        "Open", "admin-user", DateTime.UtcNow,
        [
            new QuestionModel("q1", "Who scores first?", 0, null,
            [
                new OptionModel("o1", "Home", 0),
                new OptionModel("o2", "Away", 1)
            ])
        ]);

    private IRenderedComponent<Poll> RenderPoll(DateTime deadline, List<UserAnswerModel>? existingAnswers = null)
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll(deadline))
            .MapJson(HttpMethod.Get, $"polls/{PollId}/answers", existingAnswers ?? []);
        Services.AddSingleton(handler.BuildClient());

        return Render<Poll>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    [Fact]
    public void AllQuestionsAnswered_SubmitButtonEnabled()
    {
        var cut = RenderPoll(
            DateTime.UtcNow.AddDays(1),
            existingAnswers: [new UserAnswerModel("q1", "o1", DateTime.UtcNow, null)]);

        cut.WaitForAssertion(() =>
        {
            var button = cut.Find("button.btn-primary");
            Assert.False(button.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void SomeQuestionsUnanswered_SubmitButtonDisabledWithHint()
    {
        var cut = RenderPoll(DateTime.UtcNow.AddDays(1));

        cut.WaitForAssertion(() =>
        {
            var button = cut.Find("button.btn-primary");
            Assert.True(button.HasAttribute("disabled"));
            Assert.Contains("Please pick an answer", cut.Markup);
        });
    }

    [Fact]
    public void DeadlinePassed_ShowsClosedBadgeAndDisablesInputs_AndHidesSubmitButton()
    {
        var cut = RenderPoll(DateTime.UtcNow.AddDays(-1));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Voting closed", cut.Markup);
            Assert.Empty(cut.FindAll("button.btn-primary"));
            var radio = cut.Find("input[type=radio]");
            Assert.True(radio.HasAttribute("disabled"));
        });
    }
}
