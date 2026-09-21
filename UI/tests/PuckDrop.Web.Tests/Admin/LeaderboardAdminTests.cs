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

public class LeaderboardAdminTests : BunitContext
{
    private static LeaderboardModel BuildLeaderboard(bool withAdjustment = false) => new("2025-26",
    [
        LeaderboardEntries.Entry("u1", "Nathan", totalPoints: 10, totalAnswered: 12, rank: 1),
        LeaderboardEntries.Entry(
            "u2", "Friend", totalPoints: withAdjustment ? 3 : 8, totalAnswered: 12, rank: 2, earnedPoints: 8,
            adjustments: withAdjustment
                ? [new LeaderboardAdjustmentModel("adj-1", -5, "Picked after puck drop")]
                : [])
    ]);

    private IRenderedComponent<LeaderboardAdmin> RenderPage(RoutingHttpMessageHandler handler)
    {
        Services.AddSingleton(handler.BuildClient());
        return Render<LeaderboardAdmin>();
    }

    private static IElement Button(IRenderedComponent<LeaderboardAdmin> cut, string visibleTextStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(visibleTextStart));

    private static RoutingHttpMessageHandler Routes(bool withAdjustment = false) =>
        new RoutingHttpMessageHandler().MapJson(HttpMethod.Get, "leaderboard", BuildLeaderboard(withAdjustment));

    // ─── Standings ──────────────────────────────────────────────────────────

    [Fact]
    public void Standings_ShowEarnedAdjustmentsAndTotalSeparately()
    {
        // Split out so it's obvious where a total came from before changing it.
        var cut = RenderPage(Routes(withAdjustment: true));

        cut.WaitForAssertion(() =>
        {
            var cells = cut.FindAll("tbody tr")[1].QuerySelectorAll("td");
            Assert.Equal("8", cells[0].TextContent.Trim());
            Assert.Equal("−5", cells[1].TextContent.Trim());
            Assert.Equal("3", cells[2].TextContent.Trim());
        });
    }

    [Fact]
    public void PlayerPicker_OffersOnlyPlayersWhoAlreadyHaveAStanding()
    {
        // Adjustments carry a denormalised name taken from the player's standing, and there is no
        // user directory, so someone who has never answered a scored poll can't be adjusted.
        var cut = RenderPage(Routes());

        cut.WaitForAssertion(() =>
        {
            var options = cut.Find("#adjust-player").QuerySelectorAll("option")
                .Select(o => o.TextContent.Trim()).ToArray();
            Assert.Equal(["Choose a player…", "Nathan", "Friend"], options);
        });
    }

    // ─── Applying ───────────────────────────────────────────────────────────

    [Fact]
    public void ApplyingWithNothingFilledIn_SaysWhatIsMissing_AndDoesNotCallTheApi()
    {
        var called = false;
        var handler = Routes().Map(HttpMethod.Post, "leaderboard/adjustments", _ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var cut = RenderPage(handler);

        cut.WaitForAssertion(() => Button(cut, "Apply adjustment"));
        Button(cut, "Apply adjustment").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose which player", cut.Find("#adjust-player-error").TextContent);
            Assert.Contains("other than zero", cut.Find("#adjust-points-error").TextContent);
            Assert.Contains("Give a reason", cut.Find("#adjust-reason-error").TextContent);
        });
        Assert.False(called);
    }

    [Fact]
    public void ApplyingADeduction_SendsSignedPointsAndTheReason()
    {
        CreateAdjustmentRequest? sent = null;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Get, "leaderboard", BuildLeaderboard(withAdjustment: true))
            .Map(HttpMethod.Post, "leaderboard/adjustments", request =>
            {
                sent = request.Content!.ReadFromJsonAsync<CreateAdjustmentRequest>().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        var cut = RenderPage(handler);

        cut.WaitForAssertion(() => cut.Find("#adjust-player"));
        cut.Find("#adjust-player").Change("u2");
        cut.Find("#adjust-points").Input("-5");
        cut.Find("#adjust-reason").Input("Picked after puck drop");
        Button(cut, "Apply adjustment").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal("u2", sent!.UserId);
            Assert.Equal(-5, sent.Points);
            Assert.Equal("Picked after puck drop", sent.Reason);
            Assert.Contains("applied to Friend", cut.Find("[role=status]").TextContent);
        });
    }

    // ─── Removing ───────────────────────────────────────────────────────────

    [Fact]
    public void AFourOhFour_SaysTheBuildMayBeStale_RatherThanSuggestingARetry()
    {
        // What sent us chasing the wrong cause once: a 404 from a route the running build doesn't
        // have, reported as "please try again" - advice that could never work.
        var handler = Routes().Map(HttpMethod.Post, "leaderboard/adjustments",
            _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = RenderPage(handler);

        cut.WaitForAssertion(() => cut.Find("#adjust-player"));
        cut.Find("#adjust-player").Change("u2");
        cut.Find("#adjust-points").Input("-5");
        cut.Find("#adjust-reason").Input("Picked after puck drop");
        Button(cut, "Apply adjustment").Click();

        cut.WaitForAssertion(() =>
        {
            var error = cut.Find("[role=alert]").TextContent;
            Assert.Contains("older build", error);
            Assert.DoesNotContain("try again", error);
        });
    }

    [Fact]
    public void TheApisOwnErrorMessage_IsShownWhenItSendsOne()
    {
        var handler = Routes().Map(HttpMethod.Post, "leaderboard/adjustments",
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new
                {
                    error = "NOT_FOUND",
                    message = "User 'u2' has no standing in season '2025-26' to adjust."
                })
            });
        var cut = RenderPage(handler);

        cut.WaitForAssertion(() => cut.Find("#adjust-player"));
        cut.Find("#adjust-player").Change("u2");
        cut.Find("#adjust-points").Input("-5");
        cut.Find("#adjust-reason").Input("Picked after puck drop");
        Button(cut, "Apply adjustment").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("has no standing", cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void Removing_AsksToConfirm_AndDoesNothingIfCancelled()
    {
        var called = false;
        var handler = Routes(withAdjustment: true)
            .Map(HttpMethod.Delete, "leaderboard/adjustments/adj-1?userId=u2&seasonId=2025-26", _ =>
            {
                called = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var cut = RenderPage(handler);

        cut.WaitForAssertion(() => Button(cut, "Remove"));
        Button(cut, "Remove").Click();

        JSInterop.VerifyInvoke("confirm");
        Assert.False(called);
    }

    [Fact]
    public void Removing_WhenConfirmed_DeletesByIdUserAndSeason()
    {
        // All three key parts are needed: the adjustment is stored under season and user too, and
        // there is no index on the id alone.
        var removed = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Get, "leaderboard", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(BuildLeaderboard(withAdjustment: !removed))
            })
            .Map(HttpMethod.Delete, "leaderboard/adjustments/adj-1?userId=u2&seasonId=2025-26", _ =>
            {
                removed = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var cut = RenderPage(handler);

        cut.WaitForAssertion(() => Button(cut, "Remove"));
        Button(cut, "Remove").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(removed);
            // The total goes back to the 8 points that were earned.
            Assert.Equal("8", cut.FindAll("tbody tr")[1].QuerySelectorAll("td")[2].TextContent.Trim());
        });
    }
}
