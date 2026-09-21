using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// The full poll lifecycle through the real UI, as an admin and a friend. One long test on
/// purpose: after login every step is in-app navigation, so it avoids extra cold boots against
/// the Lambda emulator.
/// </summary>
/// <remarks>
/// Create a two-question poll → Draft stays off Home → Publish → friend votes → Close → Score one
/// right and one wrong → Results shows the friend's name and both marks → Leaderboard shows 1.
/// A second poll then checks the total accumulates to 2. The "voting closed" state is covered by
/// the bUnit PollTests instead.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class PollLifecycleTests(AppHostFixture fixture)
{
    [Fact]
    public async Task FullLifecycle_CreatePublishVoteCloseScore_ThenASecondPoll_AccumulatesPoints()
    {
        var adminSession = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var adminContext = await fixture.NewAuthenticatedBrowserContextAsync(adminSession);
        var admin = await adminContext.NewPageAsync();
        admin.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        // ── Poll 1: two questions, friend gets one right and one wrong ─────────────────────
        var poll1Title = $"E2E Poll {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, poll1Title);
        await AdminPollActions.AddQuestionAsync(admin, "Will the home team win?", "Yes", "No");
        await AdminPollActions.AddQuestionAsync(admin, "Total goals over 5.5?", "Over", "Under");

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var poll1Row = admin.Locator("tr", new PageLocatorOptions { HasText = poll1Title });
        await Assertions.Expect(poll1Row.GetByText("Draft")).ToBeVisibleAsync();

        // Not active yet - Draft polls don't show up as the "Next Game".
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title }))
            .Not.ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll1Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync();
        await Assertions.Expect(poll1Row.GetByText("Open")).ToBeVisibleAsync();

        // ── Friend: now visible as the active poll, votes, submits ─────────────────────────
        var friendSession = await fixture.LoginAndCaptureSessionAsync(TestData.FriendUsername, TestData.FriendPassword);
        await using var friendContext = await fixture.NewAuthenticatedBrowserContextAsync(friendSession);
        var friend = await friendContext.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(friend, fixture.BlazorBaseUri.ToString());
        await Assertions.Expect(friend.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title }))
            .ToBeVisibleAsync();
        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Make your picks" }).ClickAsync();

        await friend.GetByLabel("Yes").CheckAsync();
        await friend.GetByLabel("Over").CheckAsync();
        await friend.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit picks" }).ClickAsync();
        await Assertions.Expect(friend.GetByText("Your picks have been submitted!")).ToBeVisibleAsync();

        // ── Admin: close, score (one right, one wrong for the friend) ──────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll1Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();
        await Assertions.Expect(poll1Row.GetByText("Closed")).ToBeVisibleAsync();

        // Closed polls drop off the "Next Game" card too - only Open ones show there.
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll1Title }))
            .Not.ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll1Row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Score" }).ClickAsync();

        // "Yes" matches the friend's pick (correct); "Under" doesn't (they picked "Over" - wrong).
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Yes" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Under" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit Scores" }).ClickAsync();

        // ── Results: friend's real display name, one correct + one incorrect mark ──────────
        await WaitForResultsAsync(admin, poll1Title);
        var resultsRow = admin.Locator("tr", new PageLocatorOptions { HasText = TestData.FriendDisplayName });
        await Assertions.Expect(resultsRow).ToBeVisibleAsync();
        // Each pick cell carries hidden ", correct" / ", wrong" text alongside its tick or cross.
        await Assertions.Expect(resultsRow.Locator("td", new LocatorLocatorOptions { HasText = ", correct" })).ToHaveCountAsync(1);
        await Assertions.Expect(resultsRow.Locator("td", new LocatorLocatorOptions { HasText = ", wrong" })).ToHaveCountAsync(1);

        // ── Leaderboard: 1 point so far ─────────────────────────────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard" }).ClickAsync();
        var leaderboardRow = admin.Locator("tr", new PageLocatorOptions { HasText = TestData.FriendDisplayName });
        await Assertions.Expect(leaderboardRow).ToBeVisibleAsync();
        // Assert the points cell, not the row: the row also carries the rank, the answered count and
        // the "1 of 2 correct · 50.0%" sub-line, so ToContainText("1") passes on almost any score.
        var friendPoints = leaderboardRow.Locator("td.lb-points");
        await Assertions.Expect(friendPoints).ToHaveTextAsync("1");

        // ── Poll 2: single question, friend gets it right - points should accumulate ───────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var poll2Title = $"E2E Poll 2 {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, poll2Title);
        await AdminPollActions.AddQuestionAsync(admin, "Will there be overtime?", "Yes", "No");

        // Published from the edit page this time - poll 1 covered publishing from the Manage polls row.
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish poll" }).ClickAsync();
        await Assertions.Expect(admin.GetByText("is now open for picks")).ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var poll2Row = admin.Locator("tr", new PageLocatorOptions { HasText = poll2Title });
        await Assertions.Expect(poll2Row.GetByText("Open")).ToBeVisibleAsync();

        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(friend.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = poll2Title }))
            .ToBeVisibleAsync();
        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Make your picks" }).ClickAsync();
        await friend.GetByLabel("Yes").CheckAsync();
        await friend.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit picks" }).ClickAsync();
        await Assertions.Expect(friend.GetByText("Your picks have been submitted!")).ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await poll2Row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();
        await Assertions.Expect(poll2Row.GetByText("Closed")).ToBeVisibleAsync();
        await poll2Row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Score" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Yes" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit Scores" }).ClickAsync();

        // Wait for scoring's redirect to Results, or it can land after the next navigation.
        await WaitForResultsAsync(admin, poll2Title);

        // ── Leaderboard again: 1 + 1 = 2, not reset to 1 ────────────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard" }).ClickAsync();
        await Assertions.Expect(friendPoints).ToHaveTextAsync("2");
    }

    /// <summary>
    /// Waits for Submit Scores to redirect to the poll's Results page. Checks the URL, because the
    /// Score page's heading is also the poll title.
    /// </summary>
    private static async Task WaitForResultsAsync(IPage admin, string title)
    {
        await admin.WaitForURLAsync(url => url.Contains("/results/"));
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = title, Level = 1 }))
            .ToBeVisibleAsync();
    }
}
