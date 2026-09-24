using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Pages.Admin;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests.Admin;

public class ImportFixturesTests : BunitContext
{
    private static readonly List<FixtureImportItemModel> TwoFixtures =
    [
        new("Belfast Giants vs Sheffield Steelers", "League",
            new DateOnly(2026, 9, 26), new DateTime(2026, 9, 26, 19, 0, 0, DateTimeKind.Utc), AlreadyImported: false),
        new("Cardiff Devils vs Nottingham Panthers", "Challenge Cup",
            new DateOnly(2026, 9, 27), new DateTime(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc), AlreadyImported: false)
    ];

    private ConfirmDialogStub Confirm { get; set; } = null!;

    private IRenderedComponent<ImportFixtures> RenderPage(RoutingHttpMessageHandler handler, bool confirmResult = true)
    {
        Services.AddSingleton(handler.BuildClient());
        Services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
        Confirm = ConfirmDialogStub.Register(this, confirmResult);
        return Render<ImportFixtures>();
    }

    private static IElement Button(IRenderedComponent<ImportFixtures> cut, string visibleTextStart) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(visibleTextStart));

    private static IElement Chip(IRenderedComponent<ImportFixtures> cut, string label) =>
        cut.FindAll(".pd-chip").First(c => c.TextContent.Trim() == label);

    private static void EnterUrlAndSearch(IRenderedComponent<ImportFixtures> cut, string url = "https://example.com/team.ics")
    {
        cut.Find("#icsUrl").Input(url);
        Button(cut, "Find fixtures").Click();
    }

    [Fact]
    public void FindFixtures_WithoutUrl_ShowsError_WithoutCallingTheApi()
    {
        var previewCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .Map(HttpMethod.Post, "fixture-imports/preview", _ => { previewCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderPage(handler);

        Button(cut, "Find fixtures").Click();

        Assert.Contains("Enter a calendar URL.", cut.Find("[role=alert]").TextContent);
        Assert.False(previewCalled);
    }

    [Fact]
    public void FindFixtures_HappyPath_ShowsResultsAndCategoryChips()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Post, "fixture-imports/preview", new FixtureImportPreviewResponseModel(TwoFixtures));
        var cut = RenderPage(handler);

        EnterUrlAndSearch(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 fixtures found", cut.Markup);
            Assert.Contains("Belfast Giants vs Sheffield Steelers", cut.Markup);
            Assert.Contains("Cardiff Devils vs Nottingham Panthers", cut.Markup);
        });

        var chips = cut.FindAll(".pd-chip").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(["All", "Challenge Cup", "League"], chips);
    }

    [Fact]
    public void CategoryChip_FiltersTheVisibleFixtureList()
    {
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Post, "fixture-imports/preview", new FixtureImportPreviewResponseModel(TwoFixtures));
        var cut = RenderPage(handler);
        EnterUrlAndSearch(cut);
        cut.WaitForAssertion(() => Assert.Contains("2 fixtures found", cut.Markup));

        Chip(cut, "League").Click();

        Assert.Contains("Belfast Giants vs Sheffield Steelers", cut.Markup);
        Assert.DoesNotContain("Cardiff Devils vs Nottingham Panthers", cut.Markup);

        // aria-pressed must be the literal string "true"/"false", not Blazor's boolean-attribute
        // present/absent treatment - .pd-chip[aria-pressed="true"] is what styles the selection.
        Assert.Equal("true", Chip(cut, "League").GetAttribute("aria-pressed"));
        Assert.Equal("false", Chip(cut, "All").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void ImportSelected_UserCancelsConfirm_CreatesNoPolls()
    {
        var createCalled = false;
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Post, "fixture-imports/preview", new FixtureImportPreviewResponseModel(TwoFixtures))
            .Map(HttpMethod.Post, "polls", _ => { createCalled = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var cut = RenderPage(handler, confirmResult: false);
        EnterUrlAndSearch(cut);
        cut.WaitForAssertion(() => Assert.Contains("2 fixtures found", cut.Markup));

        Button(cut, "Import 2 fixtures").Click();

        cut.WaitForAssertion(() => Assert.NotNull(Confirm.LastRequest));
        Assert.False(createCalled);
    }

    [Fact]
    public void ImportSelected_UserConfirms_CreatesOnePollPerSelectedFixture_AndShowsSummary()
    {
        var createdTitles = new List<string>();
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Post, "fixture-imports/preview", new FixtureImportPreviewResponseModel(TwoFixtures))
            .Map(HttpMethod.Post, "polls", request =>
            {
                var body = request.Content!.ReadFromJsonAsync<CreatePollRequest>().GetAwaiter().GetResult();
                createdTitles.Add(body!.Title);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PollModel(
                        "poll-x", "2026-27", body.GameDate, body.Title, body.Deadline, "Draft", "admin", DateTime.UtcNow))
                };
            });
        var cut = RenderPage(handler);
        EnterUrlAndSearch(cut);
        cut.WaitForAssertion(() => Assert.Contains("2 fixtures found", cut.Markup));

        Button(cut, "Import 2 fixtures").Click();

        cut.WaitForAssertion(() => Assert.Contains("2 fixtures imported as draft polls.", cut.Markup));
        Assert.Equal(
            TwoFixtures.Select(f => f.Title).OrderBy(t => t),
            createdTitles.OrderBy(t => t));
    }

    [Fact]
    public void ImportSelected_OnlySelectedFixturesAreCreated()
    {
        var createdTitles = new List<string>();
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Post, "fixture-imports/preview", new FixtureImportPreviewResponseModel(TwoFixtures))
            .Map(HttpMethod.Post, "polls", request =>
            {
                var body = request.Content!.ReadFromJsonAsync<CreatePollRequest>().GetAwaiter().GetResult();
                createdTitles.Add(body!.Title);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PollModel(
                        "poll-x", "2026-27", body.GameDate, body.Title, body.Deadline, "Draft", "admin", DateTime.UtcNow))
                };
            });
        var cut = RenderPage(handler);
        EnterUrlAndSearch(cut);
        cut.WaitForAssertion(() => Assert.Contains("2 fixtures found", cut.Markup));

        // Uncheck the first fixture row - only the second should be imported.
        cut.FindAll("input[type=checkbox]")[0].Change(false);

        Button(cut, "Import 1 fixture").Click();

        cut.WaitForAssertion(() => Assert.Contains("1 fixture imported as draft polls.", cut.Markup));
        Assert.Equal(["Cardiff Devils vs Nottingham Panthers"], createdTitles);
    }

    [Fact]
    public void AlreadyImportedFixture_StartsUnselected_AndIsBadged_ButStillCreatableIfReChecked()
    {
        List<FixtureImportItemModel> fixtures =
        [
            TwoFixtures[0] with { AlreadyImported = true },
            TwoFixtures[1]
        ];
        var createdTitles = new List<string>();
        var handler = new RoutingHttpMessageHandler()
            .MapJson(HttpMethod.Post, "fixture-imports/preview", new FixtureImportPreviewResponseModel(fixtures))
            .Map(HttpMethod.Post, "polls", request =>
            {
                var body = request.Content!.ReadFromJsonAsync<CreatePollRequest>().GetAwaiter().GetResult();
                createdTitles.Add(body!.Title);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PollModel(
                        "poll-x", "2026-27", body.GameDate, body.Title, body.Deadline, "Draft", "admin", DateTime.UtcNow))
                };
            });
        var cut = RenderPage(handler);
        EnterUrlAndSearch(cut);

        // Only the second (not already imported) fixture is selected by default.
        cut.WaitForAssertion(() => Assert.Contains("1 of 2 selected", cut.Markup));
        Assert.Contains("Already imported", cut.Markup);

        // The admin can still re-check it - "already imported" is a hint, not a hard block.
        cut.FindAll("input[type=checkbox]")[0].Change(true);
        Button(cut, "Import 2 fixtures").Click();

        cut.WaitForAssertion(() => Assert.Contains("2 fixtures imported as draft polls.", cut.Markup));
        Assert.Equal(2, createdTitles.Count);
    }
}
