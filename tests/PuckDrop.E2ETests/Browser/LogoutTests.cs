using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Logs in fresh rather than using a cached session, because logging out ends the Keycloak session
/// other tests' cached sessions rely on.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class LogoutTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Logout_ClearsSession_SubsequentProtectedNavigationRedirectsToLogin()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, fixture.BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));

        await page.FillAsync("#username", TestData.FriendUsername);
        await page.FillAsync("#password", TestData.FriendPassword);
        await page.ClickAsync("#kc-login");

        var logoutButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Log out" });
        await Assertions.Expect(logoutButton).ToBeVisibleAsync();

        // persist-login.js revokes the refresh token before the logout redirect.
        var revokeRequest = page.WaitForRequestAsync(request => request.Url.Contains("/protocol/openid-connect/revoke"));

        await logoutButton.ClickAsync();

        await revokeRequest;

        // Returning from Keycloak's logout reruns the bootstrap fetch, which can fail transiently.
        // Logout already happened by then, so a reload is enough to retry it.
        var loggedOut = page.GetByText("You've been logged out.");
        await fixture.AwaitBootstrapAsync(page, loggedOut);

        await Assertions.Expect(loggedOut).ToBeVisibleAsync();

        var persistedUserKeys = await page.EvaluateAsync<string[]>(
            "() => Object.keys(localStorage).filter(key => key.startsWith('puckdrop.oidc.'))");
        Assert.Empty(persistedUserKeys);

        // In-app navigation re-checks auth without another cold bootstrap.
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard" }).ClickAsync();

        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));
        Assert.Equal("Sign in to PuckDrop", await page.TitleAsync());
    }
}
