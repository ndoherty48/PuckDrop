using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// The real-browser version of the bUnit AppTests auth-config failure test.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class AuthConfigFailureTests(AppHostFixture fixture)
{
    [Fact]
    public async Task AuthConfigUnreachable_ShowsErrorPage_NotABlankPage()
    {
        await using var context = await fixture.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        // Page routes win over the fixture's context route, so only auth-config fails.
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
