using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests;

/// <summary>
/// Confirms the whole mechanics work before any real coverage is built on top: the AppHost boots
/// for real, and a real browser navigating to the real Blazor app gets all the way through the
/// unauthenticated boot sequence (fetch /auth-config from the real API, then the real OIDC
/// redirect) to a real Keycloak login page. Delete once other tests exercise the same path.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class SanityTests(AppHostFixture fixture)
{
    [Fact]
    public async Task AppBoots_AndUnauthenticatedNavigationReachesRealKeycloakLogin()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();
        var consoleMessages = new List<string>();
        page.Console += (_, msg) => consoleMessages.Add($"[{msg.Type}] {msg.Text}");
        page.PageError += (_, error) => consoleMessages.Add($"[pageerror] {error}");

        await page.GotoAsync(fixture.BlazorBaseUri.ToString());
        try
        {
            // Generous timeout: this is the first-ever load of a freshly-built WASM app in a
            // cold headless browser profile (download + JIT-warm the whole runtime), not a warm
            // reload.
            await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"), new PageWaitForURLOptions
            {
                Timeout = 60_000
            });
        }
        catch
        {
            Console.WriteLine($"[DIAG] Final URL: {page.Url}");
            Console.WriteLine($"[DIAG] Title: {await page.TitleAsync()}");
            Console.WriteLine($"[DIAG] Body text: {await page.InnerTextAsync("body")}");
            Console.WriteLine($"[DIAG] Console messages ({consoleMessages.Count}):");
            foreach (var msg in consoleMessages)
                Console.WriteLine($"[DIAG]   {msg}");
            throw;
        }

        Assert.Contains("realms/PuckDrop", page.Url);
        Assert.Equal("Sign in to PuckDrop", await page.TitleAsync());
    }
}
