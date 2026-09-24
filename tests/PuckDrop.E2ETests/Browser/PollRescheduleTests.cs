using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Editing a Draft poll's game date and deadline through the real UI, down to real DynamoDB.
/// </summary>
/// <remarks>
/// This is the layer that matters for this feature specifically: the season-collection copy of a
/// poll is keyed by (season, game date) in a DynamoDB sort key, so a naive save that changes the
/// game date without deleting the old key leaves a stale duplicate behind - a poll repository
/// unit test with a fake can't catch that, only a real save against real DynamoDB can. The
/// season-crossing rejection is unit-tested in isolation too (PollServiceTests), but proving the
/// real 400 actually reaches and stays inside the modal is a full-stack concern.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class PollRescheduleTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Reschedule_ThenReschedule_NoDuplicateInManagePolls_AndLockedOncePublished()
    {
        var adminSession = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var adminContext = await fixture.NewAuthenticatedBrowserContextAsync(adminSession);
        var admin = await adminContext.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        // ── Create a Draft poll and give it a question, so it's publishable later ──────────
        var title = $"E2E Reschedule {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, title);
        await AdminPollActions.AddQuestionAsync(admin, "Will the home team win?", "Yes", "No");

        // A few days out, comfortably within the same EIHL season as today for any date this
        // suite realistically runs on (the Aug 1 season boundary is the one exception).
        var originalGameDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var newGameDate = originalGameDate.AddDays(5);
        var newDeadline = newGameDate.ToDateTime(new TimeOnly(19, 0));

        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit date and time" }).ClickAsync();
        await admin.FillAsync("#rescheduleDate", newGameDate.ToString("yyyy-MM-dd"));
        await admin.FillAsync("#rescheduleDeadline", newDeadline.ToString("yyyy-MM-ddTHH:mm"));
        await admin.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Assertions.Expect(admin.GetByText("Date and time updated.")).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText(newGameDate.ToString("ddd d MMM yyyy"))).ToBeVisibleAsync();

        // ── The regression check this whole test exists for: exactly one row, at the new date,
        //    not one at each of the old and new dates ─────────────────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Manage polls" }).ClickAsync();
        var pollRow = admin.Locator("tr", new PageLocatorOptions { HasText = title });
        await Assertions.Expect(pollRow).ToHaveCountAsync(1);
        await Assertions.Expect(pollRow.GetByRole(AriaRole.Cell, new LocatorGetByRoleOptions
        {
            Name = newGameDate.ToString("ddd d MMM yyyy")
        })).ToBeVisibleAsync();

        // ── Reschedule again with the same date (the no-delete branch) - still just one row ──
        await pollRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Edit" }).ClickAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit date and time" }).ClickAsync();
        await admin.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Assertions.Expect(admin.GetByText("Date and time updated.")).ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Manage polls" }).ClickAsync();
        await Assertions.Expect(admin.Locator("tr", new PageLocatorOptions { HasText = title })).ToHaveCountAsync(1);

        // ── Publish it, then confirm the edit control is really gone server-side too, not just
        //    hidden by the client - reload the page rather than trust in-memory state ─────────
        pollRow = admin.Locator("tr", new PageLocatorOptions { HasText = title });
        await pollRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Publish" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Publish");
        await Assertions.Expect(pollRow.GetByText("Open")).ToBeVisibleAsync();

        await pollRow.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Edit" }).ClickAsync();
        await fixture.ReloadOnBootstrapFailureAsync(admin);
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = title, Level = 1 }))
            .ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit date and time" }))
            .Not.ToBeVisibleAsync();

        // ── A second Draft poll: the server rejects a reschedule that crosses into a different
        //    season, and the error surfaces inside the still-open modal, not as a page navigation
        //    or a silently-accepted change ─────────────────────────────────────────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Manage polls" }).ClickAsync();
        var secondTitle = $"E2E Reschedule Season {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, secondTitle);

        var crossSeasonDate = originalGameDate.AddYears(1); // always a different EIHL season ID
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit date and time" }).ClickAsync();
        await admin.FillAsync("#rescheduleDate", crossSeasonDate.ToString("yyyy-MM-dd"));
        await admin.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Assertions.Expect(admin.GetByText("The new game date must stay within the current season."))
            .ToBeVisibleAsync();
        // Still shows the poll's real, unchanged date underneath the open modal.
        await Assertions.Expect(admin.GetByText(originalGameDate.ToString("ddd d MMM yyyy"))).ToBeVisibleAsync();

        // The modal (correctly) stays open on the rejection - its backdrop blocks the rest of the
        // page, so it has to be dismissed before navigating away.
        await admin.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();

        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Manage polls" }).ClickAsync();
        var secondRow = admin.Locator("tr", new PageLocatorOptions { HasText = secondTitle });
        await Assertions.Expect(secondRow.GetByRole(AriaRole.Cell, new LocatorGetByRoleOptions
        {
            Name = originalGameDate.ToString("ddd d MMM yyyy")
        })).ToBeVisibleAsync();
    }
}
