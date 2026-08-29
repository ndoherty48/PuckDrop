using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Exercises App.razor's real AuthorizeRouteView.NotAuthorized branching by navigating straight
/// to an admin-only URL under each of the three states it distinguishes: authenticated+authorized,
/// authenticated-but-not-authorized (renders Pages/Unauthorized.razor), and unauthenticated
/// (redirects to login). Also the first real use of AppHostFixture's storage-state reuse helper,
/// so a login here starts an already-authenticated context instead of repeating the login UI.
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
