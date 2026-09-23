using Microsoft.Playwright;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Admin steps shared by more than one browser test: creating a poll and adding a question to it.
/// Both leave the page on that poll's edit screen.
/// </summary>
internal static class AdminPollActions
{
    /// <summary>
    /// Creates a poll from the admin pages, with a deadline far enough ahead that voting is still
    /// open after a slow run. Starts from any page with the "Create Poll" link.
    /// </summary>
    public static async Task CreatePollAsync(IPage admin, string title)
    {
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create Poll" }).ClickAsync();

        var gameDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = DateTime.UtcNow.AddHours(6);

        await admin.FillAsync("#title", title);
        await admin.FillAsync("#gameDate", gameDate.ToString("yyyy-MM-dd"));
        await admin.FillAsync("#deadline", deadline.ToString("yyyy-MM-ddTHH:mm"));
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Poll" }).ClickAsync();

        // The edit page's h1 is the poll's own title
        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = title, Level = 1 }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Fills EditPoll's "Add a question" form by its labels: the question field, then the first
    /// two option fields.
    /// </summary>
    public static async Task AddQuestionAsync(IPage admin, string questionText, string option1, string option2)
    {
        var addQuestion = admin.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = "Add a question" });

        await addQuestion.GetByLabel("Question", new LocatorGetByLabelOptions { Exact = true }).FillAsync(questionText);
        await addQuestion.GetByLabel("Option 1", new LocatorGetByLabelOptions { Exact = true }).FillAsync(option1);
        await addQuestion.GetByLabel("Option 2", new LocatorGetByLabelOptions { Exact = true }).FillAsync(option2);
        await addQuestion.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add question" })
            .ClickAsync();

        await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = questionText }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Clicks the confirm button of the design-system confirm dialog (Components/Modal.razor via
    /// ConfirmDialogHost) that now answers Publish/Close voting/Submit scores/Restore/Remove -
    /// window.confirm() no longer fires for any of them.
    /// </summary>
    public static async Task ConfirmAsync(IPage page, string buttonName)
    {
        await page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = buttonName, Exact = true })
            .ClickAsync();
    }
}
