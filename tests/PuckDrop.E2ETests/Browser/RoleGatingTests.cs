using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Navigates straight to an admin-only page as an admin, as a friend (not authorized page) and
/// anonymously (redirect to login).
/// </summary>
[Collection(E2ETestCollection.Name)]
public class RoleGatingTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Admin_NavigatingDirectlyToAdminPolls_SeesThePage()
    {
        var session = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var context = await fixture.NewAuthenticatedBrowserContextAsync(session);
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Manage Polls" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Friend_NavigatingDirectlyToAdminPolls_SeesUnauthorizedPage_NotThePolls()
    {
        var session = await fixture.LoginAndCaptureSessionAsync(TestData.FriendUsername, TestData.FriendPassword);
        await using var context = await fixture.NewAuthenticatedBrowserContextAsync(session);
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        await Assertions.Expect(page.GetByText("You don't have permission to view this page."))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Manage Polls" }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AnonymousUser_NavigatingDirectlyToAdminPolls_IsRedirectedToLogin()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, new Uri(fixture.BlazorBaseUri, "admin/polls").ToString());

        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));
        Assert.Equal("Sign in to PuckDrop", await page.TitleAsync());
    }
}
