using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Services;
using PuckDrop.Application.Services.Abstractions;
using Xunit;

namespace PuckDrop.Application.Tests;

public class FixtureImportServiceTests
{
    private const string IcsUrl = "https://eihl-calendars.example/BelfastGiants.ics";

    private static FixtureCandidate Fixture(DateOnly gameDate, string title = "Belfast Giants vs Sheffield Steelers") =>
        new(title, "CHL", gameDate, gameDate.ToDateTime(new TimeOnly(19, 0)));

    private static (FixtureImportService Service, IIcsFixtureFeedFetcher Fetcher) CreateService(
        params FixtureCandidate[] fixtures)
    {
        var fetcher = Substitute.For<IIcsFixtureFeedFetcher>();
        fetcher.FetchAsync(IcsUrl, Arg.Any<CancellationToken>()).Returns(fixtures.ToList());
        return (new FixtureImportService(fetcher), fetcher);
    }

    [Fact]
    public async Task PreviewAsync_FiltersOutFixturesBeforeStartDate()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var (service, _) = CreateService(
            Fixture(startDate.AddDays(-1)),
            Fixture(startDate),
            Fixture(startDate.AddDays(1)));

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.True(f.GameDate >= startDate));
    }

    [Fact]
    public async Task PreviewAsync_NoEndDate_IncludesEverythingFromStartDateOnward()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var (service, _) = CreateService(Fixture(startDate.AddYears(1)));

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    [Fact]
    public async Task PreviewAsync_EndDateGiven_ExcludesFixturesAfterIt()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var endDate = new DateOnly(2026, 1, 31);
        var (service, _) = CreateService(
            Fixture(new DateOnly(2026, 1, 20)),
            Fixture(new DateOnly(2026, 2, 1))); // spans into the next month - excluded

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 1, 20), result[0].GameDate);
    }

    [Fact]
    public async Task PreviewAsync_OrdersByDeadline()
    {
        var startDate = new DateOnly(2026, 1, 1);
        var (service, _) = CreateService(
            Fixture(new DateOnly(2026, 1, 20), "Later"),
            Fixture(new DateOnly(2026, 1, 10), "Earlier"));

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.Equal(["Earlier", "Later"], result.Select(f => f.Title));
    }
}
