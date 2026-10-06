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

    private static PollDetailModel BuildPoll(params QuestionModel[] questions) => BuildPoll("Draft", questions);

    private static PollDetailModel BuildPoll(string status, params QuestionModel[] questions) => new(
        PollId, "2026-27", new DateOnly(2026, 9, 23), "Belfast Giants vs Guildford Flames",
        new DateTime(2026, 9, 23, 19, 15, 0), status, "admin", DateTime.UtcNow, questions.ToList());

    // What the fake API currently returns for the poll - tests replace it to simulate the reload
    // that follows an add or delete.
    private PollDetailModel _currentPoll = BuildPoll(Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));

    private ConfirmDialogStub Confirm { get; set; } = null!;

    private IRenderedComponent<EditPoll> RenderEditPoll(RoutingHttpMessageHandler? handler = null, bool confirmResult = true)
    {
        handler ??= new RoutingHttpMessageHandler();
        handler.Map(HttpMethod.Get, $"polls/{PollId}", _ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_currentPoll) });
        Services.AddSingleton(handler.BuildClient());
        Services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
        Confirm = ConfirmDialogStub.Register(this, confirmResult);

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

    // ─── The next-step panel ────────────────────────────────────────────────

    [Fact]
    public void Publish_UserConfirms_OpensThePoll_WithoutLeavingThePage()
    {
        var publishCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/publish", _ =>
            {
                publishCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(_currentPoll with { Status = "Open" })
                };
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Publish"));

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(publishCalled);
            Assert.Contains("“Belfast Giants vs Guildford Flames” is now open for picks.", cut.Find("[role=status]").TextContent);
            // Still on the edit page, now showing what comes next for an Open poll
            Assert.Equal("Belfast Giants vs Guildford Flames", cut.Find("h1").TextContent.Trim());
            Assert.Equal("Open for picks", cut.Find("#next-step-title").TextContent.Trim());
            Assert.Equal("Close voting", Button(cut, "Close voting").TextContent.Trim());
            Assert.NotNull(cut.Find($"a[href='poll/{PollId}']"));
        });
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void Publish_UserCancelsConfirm_DoesNotCallTheApi()
    {
        var publishCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/publish", _ => { publishCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderEditPoll(handler, confirmResult: false);
        cut.WaitForAssertion(() => Button(cut, "Publish"));

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => Assert.NotNull(Confirm.LastRequest));
        Assert.False(publishCalled);
        Assert.Equal("Ready to publish?", cut.Find("#next-step-title").TextContent.Trim());
    }

    [Fact]
    public void Publish_WithNoQuestions_SaysWhatsMissing_WithoutCallingTheApi()
    {
        // Publish stays enabled with nothing to answer; it points at the empty form instead.
        _currentPoll = BuildPoll();
        var cut = RenderEditPoll();
        cut.WaitForAssertion(() => Button(cut, "Publish"));

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Add a question before publishing.", cut.Find("[role=alert]").TextContent);
            Assert.DoesNotContain("Failed to publish poll", cut.Markup);
        });
        Assert.Null(Confirm.LastRequest);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void Publish_RejectedByTheApiAsEmpty_ShowsThatReason_NotTheGenericError()
    {
        // The poll has a question here but not on the server - another admin deleted the last one.
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/publish", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error = "INVALID_OPERATION", message = "Cannot publish a poll with no questions. Add at least one question first." })
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Publish"));

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Add a question before publishing.", cut.Find("[role=alert]").TextContent);
            Assert.DoesNotContain("Failed to publish poll", cut.Markup);
            Assert.Equal("Ready to publish?", cut.Find("#next-step-title").TextContent.Trim());
        });
    }

    [Fact]
    public void Publish_Fails_ShowsAnError_AndLeavesItInDraft()
    {
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/publish", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Publish"));

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Failed to publish poll. Please try again.", cut.Find("[role=alert]").TextContent);
            Assert.Equal("Ready to publish?", cut.Find("#next-step-title").TextContent.Trim());
        });
    }

    [Fact]
    public void CloseVoting_OnAnOpenPoll_AnnouncesIt_AndOffersScoring()
    {
        _currentPoll = BuildPoll("Open", Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/close", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(_currentPoll with { Status = "Closed" })
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Close voting"));

        Button(cut, "Close voting").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Voting is closed for “Belfast Giants vs Guildford Flames”.", cut.Find("[role=status]").TextContent);
            Assert.Equal("Voting closed", cut.Find("#next-step-title").TextContent.Trim());
            Assert.NotNull(cut.Find($"a[href='admin/polls/{PollId}/score']"));
        });
    }

    // ─── Reschedule (Draft only) ────────────────────────────────────────────

    [Fact]
    public void Reschedule_Draft_ShowsTheEditButton()
    {
        var cut = RenderEditPoll();

        cut.WaitForAssertion(() => Assert.NotNull(Button(cut, "Edit date and time")));
    }

    [Theory]
    [InlineData("Open", "Open for picks")]
    [InlineData("Closed", "Voting closed")]
    [InlineData("Scored", "Poll scored")]
    public void Reschedule_NotDraft_HidesTheEditButton(string status, string expectedNextStepTitle)
    {
        _currentPoll = BuildPoll(status, Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));

        var cut = RenderEditPoll();

        cut.WaitForAssertion(() => Assert.Equal(expectedNextStepTitle, cut.Find("#next-step-title").TextContent.Trim()));
        // The modal's own (always-rendered, natively-hidden) title text also reads "Edit date and
        // time", so check for the trigger button specifically rather than the string anywhere.
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Edit date and time");
    }

    [Fact]
    public void Reschedule_Save_UpdatesTheDisplayedDateAndTime()
    {
        JSInterop.SetupVoid("puckDropDialog.show", _ => true).SetVoidResult();
        JSInterop.SetupVoid("puckDropDialog.close", _ => true).SetVoidResult();

        var newDate = new DateOnly(2026, 9, 30);
        // Admin types 18:00 in their own timezone (BST, UTC+1 in late September); the server is
        // sent and returns the UTC equivalent, and the page converts it back to 18:00 for display.
        var newDeadlineLocal = new DateTime(2026, 9, 30, 18, 0, 0);
        var newDeadlineUtc = new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);
        string? submittedBody = null;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/reschedule", request =>
            {
                submittedBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PollModel(
                        PollId, "2026-27", newDate, _currentPoll.Title, newDeadlineUtc, "Draft", "admin", DateTime.UtcNow))
                };
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Edit date and time"));

        Button(cut, "Edit date and time").Click();
        cut.Find("#rescheduleDate").Change(newDate.ToString("yyyy-MM-dd"));
        cut.Find("#rescheduleDeadline").Change(newDeadlineLocal.ToString("yyyy-MM-ddTHH:mm:ss"));
        Button(cut, "Save").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Date and time updated.", cut.Find("[role=status]").TextContent);
            Assert.Contains(newDate.ToString("ddd d MMM yyyy"), cut.Markup);
            Assert.Contains(newDeadlineLocal.ToString("ddd d MMM, HH:mm"), cut.Markup);
        });
        Assert.NotNull(submittedBody);
        Assert.Contains("\"gameDate\":\"2026-09-30\"", submittedBody);
        Assert.Contains("\"deadline\":\"2026-09-30T17:00:00", submittedBody);
    }

    [Fact]
    public void Reschedule_RejectedByTheApi_ShowsTheErrorInsideTheModal_NotAsThePageError()
    {
        JSInterop.SetupVoid("puckDropDialog.show", _ => true).SetVoidResult();
        JSInterop.SetupVoid("puckDropDialog.close", _ => true).SetVoidResult();

        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/reschedule", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error = "INVALID_OPERATION", message = "The new game date must stay within the current season." })
            });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Edit date and time"));

        Button(cut, "Edit date and time").Click();
        Button(cut, "Save").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("The new game date must stay within the current season.", cut.Find(".pd-dialog").TextContent));
        // Not surfaced as the page-level error banner (.edit-message) - the admin is still mid-edit.
        Assert.Empty(cut.FindAll(".edit-message"));
    }

    [Fact]
    public void Reschedule_Cancel_DiscardsChanges_WithoutCallingTheApi()
    {
        JSInterop.SetupVoid("puckDropDialog.show", _ => true).SetVoidResult();
        JSInterop.SetupVoid("puckDropDialog.close", _ => true).SetVoidResult();

        var rescheduleCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, $"polls/{PollId}/reschedule", _ => { rescheduleCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderEditPoll(handler);
        cut.WaitForAssertion(() => Button(cut, "Edit date and time"));

        Button(cut, "Edit date and time").Click();
        cut.Find("#rescheduleDate").Change("2026-10-15");
        Button(cut, "Cancel").Click();

        Assert.False(rescheduleCalled);
        Assert.Contains(_currentPoll.GameDate.ToString("ddd d MMM yyyy"), cut.Markup);
    }

    [Fact]
    public void ClosedPoll_OffersScoring_AndDropsTheQuestionEditor()
    {
        // The API rejects question edits once voting has closed, so the controls go too.
        _currentPoll = BuildPoll("Closed", Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));

        var cut = RenderEditPoll();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Voting closed", cut.Find("#next-step-title").TextContent.Trim());
            Assert.NotNull(cut.Find($"a[href='admin/polls/{PollId}/score']"));
            Assert.Empty(cut.FindAll("#add-question-title"));
            Assert.Empty(cut.FindAll("button").Where(b => b.TextContent.Trim().StartsWith("Delete")));
        });
    }

    [Fact]
    public void ScoredPoll_PointsAtTheResults()
    {
        _currentPoll = BuildPoll("Scored", Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));

        var cut = RenderEditPoll();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Poll scored", cut.Find("#next-step-title").TextContent.Trim());
            Assert.NotNull(cut.Find($"a[href='results/{PollId}']"));
        });
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

    private RoutingHttpMessageHandler WithParticipation(int picked, int players, params string[] stillToPick) =>
        new RoutingHttpMessageHandler().MapJson(HttpMethod.Get, $"polls/{PollId}/participation",
            new PollParticipationDetailModel(PollId, picked, players,
                stillToPick.Select(name => new PlayerModel($"u-{name}", name)).ToList()));

    [Fact]
    public void Open_ShowsPickCountAndWhoIsStillToPick()
    {
        _currentPoll = BuildPoll("Open", Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));
        var cut = RenderEditPoll(WithParticipation(5, 9, "Ciaran", "Dee", "Mark", "Sinead"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("5 of 9 players picked", cut.Find(".pd-meta-row .pd-badge .pd-sr-only").TextContent);
            Assert.Equal("Still to pick (4)", cut.Find("#still-to-pick-title").TextContent.Trim());
            Assert.Equal(new[] { "Ciaran", "Dee", "Mark", "Sinead" }, cut.FindAll(".edit-picker").Select(p => p.TextContent.Trim()));
        });
    }

    [Fact]
    public void Open_EveryonesPicked_SaysSo()
    {
        _currentPoll = BuildPoll("Open", Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));
        var cut = RenderEditPoll(WithParticipation(9, 9));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Still to pick", cut.Find("#still-to-pick-title").TextContent.Trim());
            Assert.Contains("Everyone's picked.", cut.Find(".edit-pickers").TextContent);
            Assert.Contains("pd-badge-open", cut.Find(".pd-meta-row .pd-badge:not(:first-child)").ClassList);
        });
    }

    [Fact]
    public void Draft_ShowsNoPickCountOrStillToPick()
    {
        var cut = RenderEditPoll(WithParticipation(0, 9));

        cut.WaitForAssertion(() => Assert.Equal("Belfast Giants vs Guildford Flames", cut.Find("h1").TextContent.Trim()));
        Assert.Single(cut.FindAll(".pd-meta-row .pd-badge"));
        Assert.Empty(cut.FindAll(".edit-pickers"));
    }

    [Fact]
    public void Open_ParticipationFailsToLoad_SaysSoWithoutBreakingThePage()
    {
        // No participation route is registered, so that call throws.
        _currentPoll = BuildPoll("Open", Question("q1", "Who wins?", 1, "Belfast Giants", "Guildford Flames"));
        var cut = RenderEditPoll();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Couldn't load who's picked.", cut.Find(".edit-pickers").TextContent);
            Assert.Single(cut.FindAll(".pd-meta-row .pd-badge"));
            Assert.Equal("Question 1: Who wins?", cut.Find(".edit-question h3").TextContent.Trim());
        });
    }
}
