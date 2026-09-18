using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Reopening the site must not go back through the identity provider - see
/// wwwroot/js/persist-login.js.
/// </summary>
/// <remarks>
/// A new page in the same context stands in for reopening: fresh sessionStorage, shared
/// localStorage and cookies. Keycloak's SSO cookie would log the user back in anyway, so the test
/// asserts no authorize request at all; a refresh-token renewal only calls the token endpoint.
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
