using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests;

/// <summary>
/// Smoke test: the AppHost boots and an anonymous visit reaches the Keycloak login page.
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

        try
        {
            await fixture.GotoWithBootstrapRetryAsync(page, fixture.BlazorBaseUri.ToString());
            // The first cold WASM load can be slow; the context's long default timeout covers it.
            await page.WaitForURLAsync(url => url.Contains("realms/PuckDrop"));
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
