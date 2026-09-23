using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Re-scoring a poll, voiding and restoring a player's picks, and deducting points - through the
/// real UI and down to real DynamoDB.
/// </summary>
/// <remarks>
/// One long test on purpose, like PollLifecycleTests: after login every step is in-app navigation,
/// so it avoids extra cold boots against the Lambda emulator.
///
/// This is the layer that matters for these features. Season totals are folded on read from three
/// kinds of fact sharing a U#{userId}# sort-key prefix, so a wrong key, a missing attribute or a
/// mis-parsed timestamp only shows up against DynamoDB itself - every test below this one hands
/// the repository a substitute. It is also the only place the undo paths run end to end: they use
/// a conditional delete, which is a DynamoDB-side behaviour.
///
/// Every assertion is relative to the friend's total when the test starts, because the leaderboard
/// is per season and other tests in the same run add to it.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class ScoringCorrectionsTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Rescoring_ThenVoiding_Restoring_AndDeducting_AllLandOnTheLeaderboard()
    {
        var adminSession = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var adminContext = await fixture.NewAuthenticatedBrowserContextAsync(adminSession);
        var admin = await adminContext.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "leaderboard").ToString());
        var baseline = await LeaderboardAssertions.PointsAsync(admin, TestData.FriendDisplayName);

        // ── A two-question poll the friend gets entirely right ─────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();

        var title = $"E2E Corrections {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, title);
        await AdminPollActions.AddQuestionAsync(admin, "Will the home team win?", "Yes", "No");
        await AdminPollActions.AddQuestionAsync(admin, "Total goals over 5.5?", "Over", "Under");

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var pollRow = admin.Locator("tr", new PageLocatorOptions { HasText = title });
        await pollRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Publish");
        await Assertions.Expect(pollRow.GetByText("Open")).ToBeVisibleAsync();

        var friendSession = await fixture.LoginAndCaptureSessionAsync(TestData.FriendUsername, TestData.FriendPassword);
        await using var friendContext = await fixture.NewAuthenticatedBrowserContextAsync(friendSession);
        var friend = await friendContext.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(friend, fixture.BlazorBaseUri.ToString());
        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Make your picks" }).ClickAsync();
        await friend.GetByLabel("Yes").CheckAsync();
        await friend.GetByLabel("Over").CheckAsync();
        await friend.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit picks" }).ClickAsync();
        await Assertions.Expect(friend.GetByText("Your picks have been submitted!")).ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await pollRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Close voting");
        await Assertions.Expect(pollRow.GetByText("Closed")).ToBeVisibleAsync();

        await pollRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Score" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Yes" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Over" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit Scores" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Submit scores");
        await admin.WaitForURLAsync(url => url.Contains("/results/"));

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, baseline + 2);

        // ── Re-score with question 2 corrected: 1 point, not 3 and not doubled ─────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await pollRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Re-score" }).ClickAsync();

        // The screen starts from the answers already recorded, so only the correction is needed.
        await Assertions.Expect(admin.GetByText("2 of 2 answers set")).ToBeVisibleAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Under" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Update scores" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Update scores");
        await admin.WaitForURLAsync(url => url.Contains("/results/"));

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, baseline + 1);

        // ── Void the friend's picks for this game day ──────────────────────────────────────
        await GoToResultsAsync(admin, title);
        await admin.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = $"Void picks for {TestData.FriendDisplayName}" }).ClickAsync();
        await admin.FillAsync("#void-reason", "Picked after puck drop");
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Void picks", Exact = true })
            .ClickAsync();
        await Assertions.Expect(admin.GetByText("picks for this game day are voided")).ToBeVisibleAsync();

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, baseline);
        await Assertions.Expect(LeaderboardAssertions.RowFor(admin, TestData.FriendDisplayName).GetByText("Picked after puck drop")).ToBeVisibleAsync();

        // ── Restore: the total comes back to exactly what it was ───────────────────────────
        await GoToResultsAsync(admin, title);
        await admin.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = $"Restore picks for {TestData.FriendDisplayName}" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Restore");
        await Assertions.Expect(admin.GetByText("picks count again")).ToBeVisibleAsync();

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, baseline + 1);
        await Assertions.Expect(LeaderboardAssertions.RowFor(admin, TestData.FriendDisplayName).GetByText("Picked after puck drop")).Not.ToBeVisibleAsync();

        // ── Deduct enough to land on exactly -5, whatever the baseline was ─────────────────
        var deduction = -(baseline + 6);

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Point adjustments" }).ClickAsync();

        // By id, like the two fields below it: the standings table underneath also has a "Player"
        // column header, so GetByLabel("Player") resolves to two elements and trips strict mode.
        await admin.Locator("#adjust-player")
            .SelectOptionAsync(new SelectOptionValue { Label = TestData.FriendDisplayName });
        await admin.FillAsync("#adjust-points", deduction.ToString());
        await admin.FillAsync("#adjust-reason", "Cause i can");
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Apply adjustment" }).ClickAsync();
        await Assertions.Expect(admin.GetByText($"applied to {TestData.FriendDisplayName}")).ToBeVisibleAsync();

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, -5);
        await Assertions.Expect(LeaderboardAssertions.RowFor(admin, TestData.FriendDisplayName).GetByText("Cause i can")).ToBeVisibleAsync();

        // Accuracy is measured on points earned, so a deduction must not touch it: 1 of 2 correct.
        await Assertions.Expect(LeaderboardAssertions.RowFor(admin, TestData.FriendDisplayName)).ToContainTextAsync("50.0%");

        // ── Re-read everything from the API: the facts are really in DynamoDB ──────────────
        await fixture.ReloadOnBootstrapFailureAsync(admin);

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, -5);
        await Assertions.Expect(
            LeaderboardAssertions.RowFor(admin, TestData.FriendDisplayName).GetByText("Cause i can"))
            .ToBeVisibleAsync();

        // ── Remove the adjustment: the total comes back exactly, and the run is left tidier ─
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Point adjustments" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = $"adjustment for {TestData.FriendDisplayName}" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Remove");
        await Assertions.Expect(admin.GetByText($"Adjustment removed for {TestData.FriendDisplayName}"))
            .ToBeVisibleAsync();

        await LeaderboardAssertions.ExpectPointsAsync(admin, TestData.FriendDisplayName, baseline + 1);
    }

    private static async Task GoToResultsAsync(IPage admin, string title)
    {
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "History" }).ClickAsync();
        await admin.Locator("li", new PageLocatorOptions { HasText = title })
            .GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "View results" }).ClickAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = title, Level = 1 }))
            .ToBeVisibleAsync();
    }
}
