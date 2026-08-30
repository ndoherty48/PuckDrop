using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// The real running app's version of PuckDrop.Web.Tests' bUnit AppTests -
/// AuthConfigLoadFailed_RendersErrorMessage_NotTheNormalRouterTree - exercising the actual WASM
/// boot sequence in a real browser instead of an in-memory component render.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class AuthConfigFailureTests(AppHostFixture fixture)
{
    [Fact]
    public async Task AuthConfigUnreachable_ShowsErrorPage_NotABlankPage()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        // Page-level routes take precedence over the context-level one AppHostFixture already
        // registers (which redirects Program.cs's hardcoded API-URL fallback to the real
        // resolved endpoint) - so this fails only auth-config specifically, while everything else
        // still resolves normally, matching a real "this one endpoint is down" scenario rather
        // than the whole backend being unreachable.
        await page.RouteAsync("**/auth-config", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 500,
            ContentType = "application/json",
            Body = "{}"
        }));

        await page.GotoAsync(fixture.BlazorBaseUri.ToString());

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions
        {
            Name = "Couldn't reach the server"
        })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Please try again later.")).ToBeVisibleAsync();
    }
}
