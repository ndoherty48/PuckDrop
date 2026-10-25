using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Cancelling a game day poll after a friend has picked, then restoring it, through the real UI
/// down to real DynamoDB.
/// </summary>
/// <remarks>
/// This is the layer that matters here: a cancel moves the poll's GSI2 status key, which is what
/// takes it off Home (the active-polls query only reads the Open key), and the cancellation lives
/// in extra poll attributes that a restore's whole-item Put has to drop again. A fake repository
/// can't show either. The leaderboard check is relative to the friend's total when the test
/// starts, because other tests in the same run score polls for them in the same season.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class CancelledPollTests(AppHostFixture fixture)
{
    private const string Reason = "Postponed: travel cancelled by the storm.";

    [Fact]
    public async Task CancelAnOpenPollWithPicks_GoneFromHome_InHistoryWithItsReason_ThenRestoreBringsItBackWithThePicks()
    {
        var adminSession = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var adminContext = await fixture.NewAuthenticatedBrowserContextAsync(adminSession);
        var admin = await adminContext.NewPageAsync();
        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        var baseline = await LeaderboardAssertions.PointsAsync(admin, TestData.FriendDisplayName);
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }).ClickAsync();

        // ── An Open poll ─────────────────────────────────────────────────────────────────────
        var title = $"E2E Cancelled {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, title);
        await AdminPollActions.AddQuestionAsync(admin, "Will the home team win?", "Yes", "No");
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Publish poll" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Publish");
        await Assertions.Expect(admin.GetByText("is now open for picks")).ToBeVisibleAsync();
        var editUrl = admin.Url;
        var pollId = editUrl.Split('/')[^2];

        // ── Friend picks ─────────────────────────────────────────────────────────────────────
        var friendSession = await fixture.LoginAndCaptureSessionAsync(TestData.FriendUsername, TestData.FriendPassword);
        await using var friendContext = await fixture.NewAuthenticatedBrowserContextAsync(friendSession);
        var friend = await friendContext.NewPageAsync();
        await fixture.GotoWithBootstrapRetryAsync(friend, new Uri(fixture.BlazorBaseUri, $"poll/{pollId}").ToString());
        await friend.GetByLabel("Yes").CheckAsync();
        await friend.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit picks" }).ClickAsync();
        await Assertions.Expect(friend.GetByText("Your picks have been submitted!")).ToBeVisibleAsync();

        // ── Admin cancels it ─────────────────────────────────────────────────────────────────
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel game" }).ClickAsync();
        var cancelDialog = admin.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Cancel this game?" });
        await cancelDialog.GetByLabel("Reason").FillAsync(Reason);
        await cancelDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel game", Exact = true }).ClickAsync();
        await Assertions.Expect(admin.GetByText("Game cancelled.")).ToBeVisibleAsync();

        // Read back from DynamoDB, not the page's in-memory state: the reason and who cancelled it
        await fixture.GotoWithBootstrapRetryAsync(admin, editUrl);
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Cancelled", Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText(Reason)).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText($"Cancelled by {TestData.AdminDisplayName}")).ToBeVisibleAsync();

        // Under Past on Manage polls, not Upcoming
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Manage polls" }).ClickAsync();
        var past = admin.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Past" });
        await Assertions.Expect(past.Locator("tr", new LocatorLocatorOptions { HasText = title })).ToContainTextAsync("Cancelled");

        // ── Friend: off Home, in History with the reason, and its page shows only that ───────
        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(friend.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Leaderboard" }).First)
            .ToBeVisibleAsync();
        await Assertions.Expect(friend.GetByText(title)).ToHaveCountAsync(0);

        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "History" }).ClickAsync();
        var historyRow = friend.Locator("li", new PageLocatorOptions { HasText = title });
        await Assertions.Expect(historyRow).ToContainTextAsync(Reason);
        await historyRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = $"View details for {title}" }).ClickAsync();
        await Assertions.Expect(friend.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "This game day was cancelled" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(friend.GetByRole(AriaRole.Radio)).ToHaveCountAsync(0);

        // ── Nothing reached the leaderboard ──────────────────────────────────────────────────
        // PointsAsync rather than ExpectPointsAsync: run on its own against a fresh table, the
        // friend has never been scored, so "unchanged" means no leaderboard row at all.
        Assert.Equal(baseline, await LeaderboardAssertions.PointsAsync(admin, TestData.FriendDisplayName));

        // ── Restore: back to Open, on Home again, with the friend's pick still there ─────────
        await fixture.GotoWithBootstrapRetryAsync(admin, editUrl);
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Restore poll" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Restore poll");
        await Assertions.Expect(admin.GetByText("is back to Open.")).ToBeVisibleAsync();

        await friend.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(friend.GetByText(title).First).ToBeVisibleAsync();

        await fixture.GotoWithBootstrapRetryAsync(friend, new Uri(fixture.BlazorBaseUri, $"poll/{pollId}").ToString());
        await Assertions.Expect(friend.GetByLabel("Yes")).ToBeCheckedAsync();
    }
}
