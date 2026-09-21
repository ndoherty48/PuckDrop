using Microsoft.Playwright;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Reading and asserting a player's leaderboard total.
/// </summary>
/// <remarks>
/// Totals are asserted relative to whatever the player had when a test started. The leaderboard is
/// per season, every test in a run shares one AppHost, and more than one test scores polls for the
/// same friend - so absolute totals only held while exactly one test did that.
/// </remarks>
internal static class LeaderboardAssertions
{
    public static ILocator RowFor(IPage page, string displayName) =>
        page.Locator("tr", new PageLocatorOptions { HasText = displayName });

    /// <summary>
    /// Goes to the leaderboard and waits for the player's points cell to read <paramref name="expected"/>.
    /// </summary>
    public static async Task ExpectPointsAsync(IPage page, string displayName, int expected)
    {
        await GoToLeaderboardAsync(page);

        // Negatives render with a real minus sign (U+2212), not a hyphen.
        var text = expected < 0 ? $"−{Math.Abs(expected)}" : expected.ToString();
        await Assertions.Expect(RowFor(page, displayName).Locator("td.lb-points")).ToHaveTextAsync(text);
    }

    /// <summary>
    /// The player's current points, or 0 when they aren't on the leaderboard yet.
    /// </summary>
    public static async Task<int> PointsAsync(IPage page, string displayName)
    {
        await GoToLeaderboardAsync(page);

        // Wait for the table or the empty state first: CountAsync doesn't auto-wait, so against a
        // still-loading page it would report 0 and silently skew every assertion that follows.
        await page.Locator(".lb-table, .lb-empty").First.WaitForAsync();

        var cell = RowFor(page, displayName).Locator("td.lb-points");
        if (await cell.CountAsync() == 0)
            return 0;

        return int.Parse((await cell.InnerTextAsync()).Trim().Replace('−', '-'));
    }

    private static Task GoToLeaderboardAsync(IPage page) =>
        // Exact, because the admin adjustments page also has a "View leaderboard" link and
        // getByRole matches the accessible name as a substring by default.
        page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leaderboard", Exact = true })
            .ClickAsync();
}
