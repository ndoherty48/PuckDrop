using System.Globalization;
using Microsoft.Playwright;
using Xunit;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// Bulk-importing fixtures from an ICS feed, through the real UI, the real API's fetch/parse, and
/// down to real DynamoDB - including re-running the same import and confirming the "already
/// imported" detection, which (like the scoring-correction undo paths) only really shows up
/// against DynamoDB itself: it depends on ISeasonRepository/IPollRepository reads for the season
/// and game dates the fetched fixtures actually touch.
/// </summary>
/// <remarks>
/// The ICS feed itself is served locally by <see cref="IcsTestServer"/> rather than a real
/// third-party site, so this suite never depends on external network access or content drift.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class FixtureImportTests(AppHostFixture fixture)
{
    [Fact]
    public async Task FindImportAndReImport_TheSecondPreviewFlagsWhatsAlreadyThere()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var home = $"E2E Import Giants {suffix}";
        var away1 = $"Opponent One {suffix}";
        var away2 = $"Opponent Two {suffix}";
        var title1 = $"{home} vs {away1}";
        var title2 = $"{home} vs {away2}";

        var date1 = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var date2 = date1.AddDays(1);

        await using var icsServer = await IcsTestServer.StartAsync(BuildIcs(home, away1, away2, date1, date2));

        var adminSession = await fixture.LoginAndCaptureSessionAsync(TestData.AdminUsername, TestData.AdminPassword);
        await using var adminContext = await fixture.NewAuthenticatedBrowserContextAsync(adminSession);
        var admin = await adminContext.NewPageAsync();

        await fixture.GotoWithBootstrapRetryAsync(admin, new Uri(fixture.BlazorBaseUri, "admin/polls/import").ToString());

        // ── First pass: neither fixture exists yet ─────────────────────────────────────────
        await admin.FillAsync("#icsUrl", icsServer.Url);
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Find fixtures" }).ClickAsync();

        await Assertions.Expect(admin.GetByText("2 fixtures found")).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText(title1)).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText(title2)).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "League" })).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cup" })).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText("Already imported")).Not.ToBeVisibleAsync();

        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Import 2 fixtures" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Import");
        await Assertions.Expect(admin.GetByText("2 fixtures imported as draft polls.")).ToBeVisibleAsync();

        // ── They're really in DynamoDB: Manage polls shows both, as drafts ──────────────────
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Back to Manage polls" }).ClickAsync();
        var row1 = admin.Locator("tr", new PageLocatorOptions { HasText = title1 });
        var row2 = admin.Locator("tr", new PageLocatorOptions { HasText = title2 });
        await Assertions.Expect(row1.GetByText("Draft")).ToBeVisibleAsync();
        await Assertions.Expect(row2.GetByText("Draft")).ToBeVisibleAsync();

        // ── Second pass over the same range: both are now flagged and unchecked by default ──
        await admin.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Import fixtures" }).ClickAsync();
        await admin.FillAsync("#icsUrl", icsServer.Url);
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Find fixtures" }).ClickAsync();

        await Assertions.Expect(admin.GetByText("2 fixtures found")).ToBeVisibleAsync();
        await Assertions.Expect(admin.GetByText("0 of 2 selected")).ToBeVisibleAsync();
        var alreadyImportedBadges = admin.GetByText("Already imported");
        await Assertions.Expect(alreadyImportedBadges).ToHaveCountAsync(2);

        // Re-checking one and importing again still works - the flag is informational, not a lock.
        await admin.Locator("input[type=checkbox]").First.CheckAsync();
        await admin.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Import 1 fixture" }).ClickAsync();
        await AdminPollActions.ConfirmAsync(admin, "Import");
        await Assertions.Expect(admin.GetByText("1 fixture imported as draft polls.")).ToBeVisibleAsync();
    }

    private static string BuildIcs(string home, string away1, string away2, DateOnly date1, DateOnly date2)
    {
        var dtStamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        string VEvent(string uid, string away, string category, DateOnly date, string time) =>
            $"""
             BEGIN:VEVENT
             UID:{uid}
             DTSTAMP:{dtStamp}
             DTSTART:{date:yyyyMMdd}T{time.Replace(":", "")}00Z
             DTEND:{date:yyyyMMdd}T220000Z
             SUMMARY:vs {away}
             DESCRIPTION:{category}: {home} vs {away} @ Test Arena on {date:dd/MM/yyyy} {time}
             CATEGORIES:{category}
             END:VEVENT
             """;

        return $"""
                BEGIN:VCALENDAR
                VERSION:2.0
                PRODID:-//PuckDrop E2E Tests//EN
                {VEvent("e2e-1", away1, "League", date1, "19:00")}
                {VEvent("e2e-2", away2, "Cup", date2, "18:30")}
                END:VCALENDAR
                """;
    }
}
