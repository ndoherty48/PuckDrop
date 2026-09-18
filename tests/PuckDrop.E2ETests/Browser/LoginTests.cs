using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Real Keycloak login for both roles; the "Admin" nav link shows only for the admin.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class LoginTests(AppHostFixture fixture)
{
    [Fact]
    public async Task AdminLogin_RedirectsToKeycloak_ThenReturnsAuthenticated_WithAdminNavVisible()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, fixture.BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));
        Assert.Equal("Sign in to PuckDrop", await page.TitleAsync());

        await page.FillAsync("#username", TestData.AdminUsername);
        await page.FillAsync("#password", TestData.AdminPassword);
        await page.ClickAsync("#kc-login");

        await Assertions.Expect(page.GetByRole(AriaRole.Banner)
                .GetByText(TestData.AdminDisplayName, new LocatorGetByTextOptions { Exact = true }))
            .ToBeVisibleAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task FriendLogin_RedirectsToKeycloak_ThenReturnsAuthenticated_WithNoAdminNav()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, fixture.BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));

        await page.FillAsync("#username", TestData.FriendUsername);
        await page.FillAsync("#password", TestData.FriendPassword);
        await page.ClickAsync("#kc-login");

        await Assertions.Expect(page.GetByRole(AriaRole.Banner)
                .GetByText(TestData.FriendDisplayName, new LocatorGetByTextOptions { Exact = true }))
            .ToBeVisibleAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }))
            .Not.ToBeVisibleAsync();
    }
}
