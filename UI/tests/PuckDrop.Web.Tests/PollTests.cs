using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
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
            ]),
            new QuestionModel("q2", "Will it go to overtime?", 1, null,
            [
                new OptionModel("o3", "Yes", 0),
                new OptionModel("o4", "No", 1)
            ])
        ]);

    private IRenderedComponent<Poll> RenderPoll(
        DateTime deadline, List<UserAnswerModel>? existingAnswers = null, RoutingHttpMessageHandler? handler = null)
    {
        handler ??= new RoutingHttpMessageHandler();
        handler
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll(deadline))
            .MapJson(HttpMethod.Get, $"polls/{PollId}/answers", existingAnswers ?? []);
        Services.AddSingleton(handler.BuildClient());

        return Render<Poll>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    private static IElement? FindSubmitButton(IRenderedComponent<Poll> cut) =>
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == "Submit picks");

    [Fact]
    public void ExistingAnswers_ArePreselected_AndCounted()
    {
        var cut = RenderPoll(
            DateTime.UtcNow.AddDays(1),
            existingAnswers: [new UserAnswerModel("q1", "o1", DateTime.UtcNow, null)]);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("input[value=o1]").HasAttribute("checked"));
            Assert.False(cut.Find("input[value=o2]").HasAttribute("checked"));
            Assert.Contains("1 of 2 answered", cut.Markup);
        });
    }

    [Fact]
    public void SubmitWithAnUnansweredQuestion_FlagsAndFocusesIt_InsteadOfSubmitting()
    {
        // No PUT route is registered, so an actual submit would throw and show the failure message.
        var cut = RenderPoll(
            DateTime.UtcNow.AddDays(1),
            existingAnswers: [new UserAnswerModel("q1", "o1", DateTime.UtcNow, null)]);

        cut.WaitForAssertion(() => Assert.NotNull(FindSubmitButton(cut)));
        var submit = FindSubmitButton(cut)!;
        Assert.False(submit.HasAttribute("disabled"));

        submit.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Pick an answer for question 2 before submitting.", cut.Find("[role=alert]").TextContent);
            Assert.Contains("Pick an answer", cut.Find("#q-q2 legend").TextContent);
            Assert.DoesNotContain("Pick an answer", cut.Find("#q-q1 legend").TextContent);
            Assert.DoesNotContain("Failed to submit", cut.Markup);
        });
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void AllAnswered_Submit_SendsTheCurrentPicks_AndConfirms()
    {
        string? submittedBody = null;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Put, $"polls/{PollId}/answers", request =>
            {
                submittedBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<UserAnswerModel>()) };
            });

        var cut = RenderPoll(
            DateTime.UtcNow.AddDays(1),
            existingAnswers:
            [
                new UserAnswerModel("q1", "o1", DateTime.UtcNow, null),
                new UserAnswerModel("q2", "o3", DateTime.UtcNow, null)
            ],
            handler);

        cut.WaitForAssertion(() => Assert.NotNull(FindSubmitButton(cut)));
        cut.Find("input[value=o4]").Change("o4");
        FindSubmitButton(cut)!.Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Your picks have been submitted!", cut.Find("[role=status]").TextContent));
        Assert.NotNull(submittedBody);
        Assert.Contains("\"o1\"", submittedBody);
        Assert.Contains("\"o4\"", submittedBody);
        Assert.DoesNotContain("\"o3\"", submittedBody);
    }

    [Fact]
    public void DeadlinePassed_ShowsVotingClosed_DisablesInputs_AndHasNoSubmitButton()
    {
        var cut = RenderPoll(DateTime.UtcNow.AddDays(-1));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Voting closed", cut.Markup);
            Assert.Null(FindSubmitButton(cut));
            Assert.All(cut.FindAll("input[type=radio]"), radio => Assert.True(radio.HasAttribute("disabled")));
        });
    }

    [Fact]
    public void PollNotFound_ShowsAnError()
    {
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Get, $"polls/{PollId}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Services.AddSingleton(handler.BuildClient());

        var cut = Render<Poll>(parameters => parameters.Add(p => p.PollId, PollId));

        cut.WaitForAssertion(() => Assert.Contains("Poll not found.", cut.Find("[role=alert]").TextContent));
    }
}
