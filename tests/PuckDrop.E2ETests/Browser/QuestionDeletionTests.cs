using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Deleting a question from a poll, through the real UI and down to real DynamoDB.
/// </summary>
/// <remarks>
/// The delete query can only fail against DynamoDB itself, so nothing below the E2E layer catches
/// it: a FilterExpression on SK (a key attribute) is a validation error, and DeleteQuestionAsync
/// used one, which made every delete a 500. The reload is the point of the test - it proves the
/// question and its options are gone from the table, not just from the page's own state.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class QuestionDeletionTests(AppHostFixture fixture)
{
    [Fact]
    public async Task DeletingAQuestion_RemovesItAndItsOptions_AndLeavesTheOtherQuestion()
    {
        var session = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var context = await fixture.NewAuthenticatedBrowserContextAsync(session);
        var admin = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        var title = $"E2E Delete {Guid.NewGuid():N}";
        await AdminPollActions.CreatePollAsync(admin, title);
        await AdminPollActions.AddQuestionAsync(admin, "First goal scorer?", "A skater", "Nobody - 0-0");
        await AdminPollActions.AddQuestionAsync(admin, "Will there be overtime?", "Yes", "No");

        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Questions (2)" }))
            .ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete question 1" }).ClickAsync();

        // The page reports a failed delete instead of throwing, so assert on the outcome it shows.
        await Assertions.Expect(admin.GetByText("Deleted “First goal scorer?”")).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText("Failed to delete question. Please try again."))
            .Not.ToBeVisibleAsync();

        // Re-read the poll from the API: the question and its options should be gone for good.
        await fixture.ReloadOnBootstrapFailureAsync(admin);

        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Questions (1)" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "First goal scorer?" }))
            .Not.ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText("Nobody - 0-0")).Not.ToBeVisibleAsync();

        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Will there be overtime?" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText("Yes", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
    }
}
