using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Drives the real login UI end to end for both roles this app cares about: real redirect to
/// Keycloak's real login page, real credentials, real redirect back, real
/// AuthorizeView/AuthorizeRouteView rendering - nothing here is mocked. Confirms the "Admin" nav
/// link (gated on the "admin" realm role, normalized client-side by
/// PuckDropClaimsPrincipalFactory) shows only for the admin user.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class LoginTests(AppHostFixture fixture)
{
    [Fact]
    public async Task AdminLogin_RedirectsToKeycloak_ThenReturnsAuthenticated_WithAdminNavVisible()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"), new PageWaitForURLOptions
        {
            Timeout = 60_000
        });
        Assert.Equal("Sign in to PuckDrop", await page.TitleAsync());

        await page.FillAsync("#username", TestData.AdminUsername);
        await page.FillAsync("#password", TestData.AdminPassword);
        await page.ClickAsync("#kc-login");

        await page.WaitForSelectorAsync($"text=Hello, {TestData.AdminDisplayName}", new PageWaitForSelectorOptions
        {
            Timeout = 30_000
        });

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task FriendLogin_RedirectsToKeycloak_ThenReturnsAuthenticated_WithNoAdminNav()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"), new PageWaitForURLOptions
        {
            Timeout = 60_000
        });

        await page.FillAsync("#username", TestData.FriendUsername);
        await page.FillAsync("#password", TestData.FriendPassword);
        await page.ClickAsync("#kc-login");

        await page.WaitForSelectorAsync($"text=Hello, {TestData.FriendDisplayName}", new PageWaitForSelectorOptions
        {
            Timeout = 30_000
        });

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Admin" }))
            .Not.ToBeVisibleAsync();
    }
}
