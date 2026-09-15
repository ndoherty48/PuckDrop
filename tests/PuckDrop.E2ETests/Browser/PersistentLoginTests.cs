using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Reopening the site must not send the user back through the identity provider. Blazor's OIDC
/// support defaults to keeping the signed-in user in per-tab sessionStorage, which closing the site
/// clears - wwwroot/js/persist-login.js moves it to localStorage so the stored refresh token
/// renews the session silently instead.
/// </summary>
/// <remarks>
/// A new page in the same browser context stands in for reopening the site: it starts with empty
/// sessionStorage but shares localStorage and cookies. Keycloak's own SSO cookie survives in the
/// context too, so "the user ends up logged in" alone would pass even without the fix (Blazor's
/// silent sign-in, or a redirect, would quietly reuse that SSO session). The test therefore asserts
/// the reopened page never hits Keycloak's authorize endpoint at all - a refresh-token renewal only
/// calls the token endpoint.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class PersistentLoginTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Login_ThenReopenInNewPage_StaysLoggedInWithoutGoingBackToKeycloak()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(page, fixture.BlazorBaseUri.ToString());
        await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));

        await page.FillAsync("#username", TestData.FriendUsername);
        await page.FillAsync("#password", TestData.FriendPassword);
        await page.ClickAsync("#kc-login");

        await page.WaitForSelectorAsync($"text=Hello, {TestData.FriendDisplayName}");
        await page.CloseAsync();

        var reopened = await context.NewPageAsync();
        var authorizeRequests = new List<string>();
        reopened.Request += (_, request) =>
        {
            if (request.Url.Contains("/protocol/openid-connect/auth"))
                authorizeRequests.Add(request.Url);
        };

        await fixture.GotoWithBootstrapRetryAsync(reopened, fixture.BlazorBaseUri.ToString());
        await reopened.WaitForSelectorAsync($"text=Hello, {TestData.FriendDisplayName}");

        Assert.Empty(authorizeRequests);

        var persistedUserKeys = await reopened.EvaluateAsync<string[]>(
            "() => Object.keys(localStorage).filter(key => key.startsWith('puckdrop.oidc.'))");
        Assert.NotEmpty(persistedUserKeys);
    }
}
