using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// The admin void/restore controls on the results page, and the public "Voided" badge.
/// </summary>
public class ResultsVoidTests : BunitContext
{
    private const string PollId = "poll-1";

    private static readonly List<QuestionModel> Questions =
    [
        new QuestionModel("q1", "Who wins?", 0, "o1",
        [
            new OptionModel("o1", "Cardiff Devils", 0),
            new OptionModel("o2", "Belfast Giants", 1)
        ])
    ];

    private static PollDetailModel BuildPoll() => new(
        PollId, "2026-27", new DateOnly(2026, 9, 12), "Cardiff Devils vs Belfast Giants",
        new DateTime(2026, 9, 12, 18, 45, 0, DateTimeKind.Utc), "Scored", "admin", DateTime.UtcNow, Questions);

    private static PollResultsModel BuildResults(bool friendVoided = false) => new(PollId, "Scored", Questions,
    [
        new UserResultModel("u1", "Nathan", [new UserAnswerModel("q1", "o1", DateTime.UtcNow, true)], 1),
        new UserResultModel("u2", "Friend", [new UserAnswerModel("q1", "o2", DateTime.UtcNow, false)], 0,
            IsVoided: friendVoided, VoidReason: friendVoided ? "Picked after puck drop" : null)
    ]);

    private ConfirmDialogStub Confirm { get; set; } = null!;

    private IRenderedComponent<Results> RenderAsAdmin(RoutingHttpMessageHandler handler, bool isAdmin = true, bool confirmResult = true)
    {
        Services.AddSingleton(handler.BuildClient());
        Confirm = ConfirmDialogStub.Register(this, confirmResult);

        var authContext = AddAuthorization();
        authContext.SetAuthorized("Nathan");
        authContext.SetClaims(new Claim("sub", "u1"));
        if (isAdmin) authContext.SetRoles("admin");

        return Render<Results>(parameters => parameters.Add(p => p.PollId, PollId));
    }

    private static RoutingHttpMessageHandler Routes(bool friendVoided = false) => new RoutingHttpMessageHandler()
        .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll())
        .MapJson(HttpMethod.Get, $"polls/{PollId}/results", BuildResults(friendVoided));

    private static IElement Button(IRenderedComponent<Results> cut, string visibleTextStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(visibleTextStart));

    private static HttpResponseMessage Json<T>(T body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    // ─── Public display ─────────────────────────────────────────────────────

    [Fact]
    public void VoidedPlayer_GetsAVoidedBadge_WithTheReasonAvailable()
    {
        var cut = RenderAsAdmin(Routes(friendVoided: true));

        cut.WaitForAssertion(() =>
        {
            var badge = cut.FindAll(".pd-badge-voided").First();
            Assert.Equal("Voided", badge.TextContent.Trim());
            Assert.Equal("Picked after puck drop", badge.GetAttribute("title"));
        });
    }

    [Fact]
    public void NonAdmin_SeesNoVoidControls()
    {
        var cut = RenderAsAdmin(Routes(), isAdmin: false);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".results-void-list")));
    }

    [Fact]
    public void AdminSection_PutsItsContentInACardBody()
    {
        // .pd-card is background and border only - it has no padding of its own. Free content
        // dropped straight into one renders flush against the card edge.
        var cut = RenderAsAdmin(Routes());

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find(".results-admin .pd-card-body");
            Assert.NotNull(body.QuerySelector(".results-void-list"));
            Assert.NotNull(body.QuerySelector(".results-admin-intro"));
        });
    }

    // ─── Voiding ────────────────────────────────────────────────────────────

    [Fact]
    public void VoidingWithoutAReason_SaysSo_AndDoesNotCallTheApi()
    {
        // The reason is public, so a blank one is refused - and the page says what's missing rather
        // than disabling the button, matching the other admin pages.
        var called = false;
        var handler = Routes().Map(HttpMethod.Post, $"polls/{PollId}/voids", _ => { called = true; return Ok(); });
        var cut = RenderAsAdmin(handler);

        cut.WaitForAssertion(() => Button(cut, "Void picks"));
        Button(cut, "Void picks").Click();
        Button(cut, "Void picks").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Give a reason", cut.Find("#void-reason-error").TextContent));
        Assert.False(called);
    }

    [Fact]
    public void VoidingWithAReason_SendsItAndReportsSuccess()
    {
        VoidPicksRequest? sent = null;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll())
            .MapJson(HttpMethod.Get, $"polls/{PollId}/results", BuildResults(friendVoided: true))
            .Map(HttpMethod.Post, $"polls/{PollId}/voids", request =>
            {
                sent = request.Content!.ReadFromJsonAsync<VoidPicksRequest>().GetAwaiter().GetResult();
                return Ok();
            });
        var cut = RenderAsAdmin(handler);

        cut.WaitForAssertion(() => Button(cut, "Void picks"));
        Button(cut, "Void picks").Click();
        cut.Find("#void-reason").Input("Picked after puck drop");
        Button(cut, "Void picks").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal("Picked after puck drop", sent!.Reason);
            Assert.Contains("are voided", cut.Find("[role=status]").TextContent);
        });
    }

    // ─── Restoring ──────────────────────────────────────────────────────────

    [Fact]
    public void Restoring_AsksToConfirmFirst_AndDoesNothingIfCancelled()
    {
        var called = false;
        var handler = Routes(friendVoided: true)
            .Map(HttpMethod.Delete, $"polls/{PollId}/voids/u2", _ => { called = true; return Ok(); });
        var cut = RenderAsAdmin(handler, confirmResult: false);

        cut.WaitForAssertion(() => Button(cut, "Restore picks"));
        Button(cut, "Restore picks").Click();

        cut.WaitForAssertion(() => Assert.NotNull(Confirm.LastRequest));
        Assert.False(called);
    }

    [Fact]
    public void Restoring_WhenConfirmed_CallsTheApiAndDropsTheVoidedBadge()
    {
        // Models the real sequence: the player is voided until the DELETE lands, after which the
        // page re-reads the results and the badge goes away.
        var restored = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, $"polls/{PollId}", BuildPoll())
            .Map(HttpMethod.Get, $"polls/{PollId}/results", _ => Json(BuildResults(friendVoided: !restored)))
            .Map(HttpMethod.Delete, $"polls/{PollId}/voids/u2", _ =>
            {
                restored = true;
                return Ok();
            });
        var cut = RenderAsAdmin(handler);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".pd-badge-voided")));
        Button(cut, "Restore picks").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(restored);
            Assert.Empty(cut.FindAll(".pd-badge-voided"));
            Assert.Contains("count again", cut.Find("[role=status]").TextContent);
        });
    }
}
