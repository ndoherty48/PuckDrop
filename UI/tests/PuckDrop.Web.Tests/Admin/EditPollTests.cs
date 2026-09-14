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

public class EditPollTests : BunitContext
{
    private const string PollId = "poll-1";

    private static QuestionModel Question(string questionId, string text, int sortOrder, params string[] options) =>
        new(questionId, text, sortOrder, null, options.Select((o, i) => new OptionModel($"{questionId}-o{i}", o, i)).ToList());

    private static PollDetailModel BuildPoll(params QuestionModel[] questions) => new(
        PollId, "2026-27", new DateOnly(2026, 9, 23), "Belfast Giants vs Guildford Flames",
        new DateTime(2026, 9, 23, 19, 15, 0), "Draft", "admin", DateTime.UtcNow, questions.ToList());

    // What the fake API currently returns for the poll - tests replace it to simulate the reload
    // that follows an add or delete.
    private PollDetailModel _currentPoll = BuildPoll(Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));

    private IRenderedComponent<EditPoll> RenderEditPoll(RoutingHttpMessageHandler? handler = null)
    {
        handler ??= new RoutingHttpMessageHandler();
        handler.Map(HttpMethod.Get, $"polls/{PollId}", _ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_currentPoll) });
        Services.AddSingleton(handler.BuildClient());

        return Render<EditPoll>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    private static IElement Button(IRenderedComponent<EditPoll> cut, string visibleTextStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(visibleTextStart));

    [Fact]
    public void ShowsThePollAndItsQuestions_WithDeleteNamingTheQuestion()
    {
        var cut = RenderEditPoll();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Belfast Giants vs Guildford Flames", cut.Find("h1").TextContent.Trim());
            Assert.Contains("Questions (1)", cut.Find("#questions-title").TextContent);
            Assert.Equal("Question 1: Who wins?", cut.Find(".edit-question h3").TextContent.Trim());
            Assert.Equal(new[] { "Belfast Giants", "Guildford Flames" }, cut.FindAll(".edit-option").Select(o => o.TextContent.Trim()));
            Assert.Equal("Delete question 1", Button(cut, "Delete").TextContent.Trim());
        });
    }

    [Fact]
    public void AddQuestion_WithBlankFields_ShowsErrors_WithoutCallingTheApi()
    {
        // No POST route is registered, so an API call would show "Failed to add question".
        var cut = RenderEditPoll();
        cut.WaitForAssertion(() => Button(cut, "Add question"));

        Button(cut, "Add question").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Enter the question", cut.Markup);
            Assert.Contains("Enter at least 2 options", cut.Markup);
            Assert.Equal("true", cut.Find("#new-question").GetAttribute("aria-invalid"));
            Assert.DoesNotContain("Failed to add question", cut.Markup);
        });
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void AddQuestion_Valid_PostsTrimmedText_ReloadsAndAnnouncesIt()
    {
        string? submittedBody = null;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/questions", request =>
            {
                submittedBody = request.Content!.ReadAsStringAsync().Result;
                _currentPoll = BuildPoll(
                    Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"),
                    Question("q2", "Will it go to overtime?", 2, "Yes", "No"));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_currentPoll.Questions[1]) };
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => cut.Find("#new-question"));

        cut.Find("#new-question").Change("Will it go to overtime?");
        cut.Find("#new-option-0").Change("Yes");
        cut.Find("#new-option-1").Change(" No ");
        Button(cut, "Add question").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Added “Will it go to overtime?”.", cut.Find("[role=status]").TextContent);
            Assert.Equal(2, cut.FindAll(".edit-question").Count);
            Assert.Equal("", cut.Find("#new-question").GetAttribute("value") ?? "");
        });
        Assert.NotNull(submittedBody);
        Assert.Contains("\"text\":\"No\"", submittedBody);
        Assert.Contains("\"sortOrder\":2", submittedBody);
    }

    [Fact]
    public void AddOption_AddsALabelledInputWithARemoveButton()
    {
        var cut = RenderEditPoll();
        cut.WaitForAssertion(() => Button(cut, "Add option"));
        Assert.Equal(2, cut.FindAll(".edit-option-row input").Count);
        Assert.Empty(cut.FindAll("button[aria-label^='Remove option']"));

        Button(cut, "Add option").Click();

        Assert.Equal(3, cut.FindAll(".edit-option-row input").Count);
        Assert.Contains("Option 3", cut.Find("label[for='new-option-2']").TextContent);

        cut.Find("button[aria-label='Remove option 3']").Click();

        Assert.Equal(2, cut.FindAll(".edit-option-row input").Count);
    }

    [Fact]
    public void DeleteQuestion_CallsTheApi_AnnouncesIt_AndMovesFocus()
    {
        var deleteCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Delete, $"polls/{PollId}/questions/q1", _ =>
            {
                deleteCalled = true;
                _currentPoll = BuildPoll();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Delete"));

        Button(cut, "Delete").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(deleteCalled);
            Assert.Contains("Deleted “Who wins?”.", cut.Find("[role=status]").TextContent);
            Assert.Contains("No questions yet", cut.Markup);
        });
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void PollNotFound_SaysSo()
    {
        // Registered first, so it wins over RenderEditPoll's own GET route.
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Get, $"polls/{PollId}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderEditPoll(handler);

        cut.WaitForAssertion(() => Assert.Equal("Poll not found", cut.Find("h1").TextContent.Trim()));
    }
}
