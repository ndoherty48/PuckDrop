using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Logs in fresh through the real UI rather than reusing AppHostFixture's cached-session helpers
/// - this test logs the user out for real, which ends their actual Keycloak SSO session, and a
/// cached session's own silent-renewal (see AppHostFixture.NewAuthenticatedBrowserContextAsync's
/// remarks) depends on that session still being alive if another test reuses it later in the run.
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

        var logoutButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Logout" });
        await Assertions.Expect(logoutButton).ToBeVisibleAsync();

        // wwwroot/js/persist-login.js revokes the refresh token (best-effort) before the logout
        // redirect, so a copy that outlived the browser session can't be reused afterwards.
        var revokeRequest = page.WaitForRequestAsync(request => request.Url.Contains("/protocol/openid-connect/revoke"));

        await logoutButton.ClickAsync();

        await revokeRequest;

        // NavigateToLogout makes a real cross-origin round trip to Keycloak's own logout
        // endpoint and back, re-running Program.cs's bootstrap fetch on return - which can hit
        // the same transient emulator-load failure a fresh page load can (confirmed live).
        try
        {
            await page.WaitForSelectorAsync("text=Couldn't reach the server", new PageWaitForSelectorOptions
            {
                Timeout = AppHostFixture.BootstrapFailureWindowMs
            });
            // The actual logout already happened server-side by this point - reload and retry
            // rather than re-click Logout.
            await fixture.ReloadOnBootstrapFailureAsync(page, maxAttempts: 3);
        }
        catch (TimeoutException)
        {
            // No failure page ever appeared - the round trip back succeeded on the first try.
        }

        await Assertions.Expect(page.GetByText("You've been logged out.")).ToBeVisibleAsync();

        var persistedUserKeys = await page.EvaluateAsync<string[]>(
            "() => Object.keys(localStorage).filter(key => key.startsWith('puckdrop.oidc.'))");
        Assert.Empty(persistedUserKeys);

        // In-SPA navigation (a nav-link click, not a fresh page load) is enough to prove the
        // session is really gone - AuthorizeRouteView re-checks auth state on every navigation
        // regardless of hard vs soft nav - and avoids the cost/risk of another cold bootstrap
        // fetch against the emulator for coverage a soft nav already gives just as faithfully.
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard" }).ClickAsync();

        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));
        Assert.Equal("Sign in to PuckDrop", await page.TitleAsync());
    }
}
