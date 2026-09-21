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
        admin.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "leaderboard").ToString());
        var baseline = await FriendPointsAsync(admin);

        // ── A two-question poll the friend gets entirely right ─────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();

        var title = $"E2E Corrections {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, title);
        await AdminPollActions.AddQuestionAsync(admin, "Will the home team win?", "Yes", "No");
        await AdminPollActions.AddQuestionAsync(admin, "Total goals over 5.5?", "Over", "Under");

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        var pollRow = admin.Locator("tr", new PageLocatorOptions { HasText = title });
        await pollRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync();
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
        await Assertions.Expect(pollRow.GetByText("Closed")).ToBeVisibleAsync();

        await pollRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Score" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Yes" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Over" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit Scores" }).ClickAsync();
        await admin.WaitForURLAsync(url => url.Contains("/results/"));

        await ExpectFriendPointsAsync(admin, baseline + 2);

        // ── Re-score with question 2 corrected: 1 point, not 3 and not doubled ─────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();
        await pollRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Re-score" }).ClickAsync();

        // The screen starts from the answers already recorded, so only the correction is needed.
        await Assertions.Expect(admin.GetByText("2 of 2 answers set")).ToBeVisibleAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Under" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Update scores" }).ClickAsync();
        await admin.WaitForURLAsync(url => url.Contains("/results/"));

        await ExpectFriendPointsAsync(admin, baseline + 1);

        // ── Void the friend's picks for this game day ──────────────────────────────────────
        await GoToResultsAsync(admin, title);
        await admin.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = $"Void picks for {TestData.FriendDisplayName}" }).ClickAsync();
        await admin.FillAsync("#void-reason", "Picked after puck drop");
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Void picks", Exact = true })
            .ClickAsync();
        await Assertions.Expect(admin.GetByText("picks for this game day are voided")).ToBeVisibleAsync();

        await ExpectFriendPointsAsync(admin, baseline);
        await Assertions.Expect(FriendRow(admin).GetByText("Picked after puck drop")).ToBeVisibleAsync();

        // ── Restore: the total comes back to exactly what it was ───────────────────────────
        await GoToResultsAsync(admin, title);
        await admin.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = $"Restore picks for {TestData.FriendDisplayName}" }).ClickAsync();
        await Assertions.Expect(admin.GetByText("picks count again")).ToBeVisibleAsync();

        await ExpectFriendPointsAsync(admin, baseline + 1);
        await Assertions.Expect(FriendRow(admin).GetByText("Picked after puck drop")).Not.ToBeVisibleAsync();

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

        await ExpectFriendPointsAsync(admin, -5);
        await Assertions.Expect(FriendRow(admin).GetByText("Cause i can")).ToBeVisibleAsync();

        // Accuracy is measured on points earned, so a deduction must not touch it: 1 of 2 correct.
        await Assertions.Expect(FriendRow(admin)).ToContainTextAsync("50.0%");

        // ── Re-read everything from the API: the facts are really in DynamoDB ──────────────
        await fixture.ReloadOnBootstrapFailureAsync(admin);

        await ExpectFriendPointsAsync(admin, -5);
        await Assertions.Expect(FriendRow(admin).GetByText("Cause i can")).ToBeVisibleAsync();
    }

    private static ILocator FriendRow(IPage admin) =>
        admin.Locator("tr", new PageLocatorOptions { HasText = TestData.FriendDisplayName });

    /// <summary>
    /// Goes to the leaderboard and waits for the friend's points cell to read <paramref name="expected"/>.
    /// </summary>
    private static async Task ExpectFriendPointsAsync(IPage admin, int expected)
    {
        // Exact, because the admin adjustments page also has a "View leaderboard" link and
        // getByRole matches the accessible name as a substring by default.
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard", Exact = true })
            .ClickAsync();

        // Negatives render with a real minus sign (U+2212), not a hyphen.
        var text = expected < 0 ? $"−{Math.Abs(expected)}" : expected.ToString();
        await Assertions.Expect(FriendRow(admin).Locator("td.lb-points")).ToHaveTextAsync(text);
    }

    /// <summary>
    /// The friend's current points, or 0 when they aren't on the leaderboard yet.
    /// </summary>
    private static async Task<int> FriendPointsAsync(IPage admin)
    {
        // Wait for the page to settle on a table or its empty state first: CountAsync doesn't
        // auto-wait, so on a still-loading page it would report 0 and silently skew every
        // assertion that follows.
        await admin.Locator(".lb-table, .lb-empty").First.WaitForAsync();

        var cell = FriendRow(admin).Locator("td.lb-points");
        if (await cell.CountAsync() == 0)
            return 0;

        var text = (await cell.InnerTextAsync()).Trim().Replace('−', '-');
        return int.Parse(text);
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
