using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Application.Services.Abstractions;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class FixtureImportServiceTests
{
    private const string IcsUrl = "https://eihl-calendars.example/BelfastGiants.ics";

    private static FixtureCandidate Fixture(DateOnly gameDate, string title = "Belfast Giants vs Sheffield Steelers") =>
        new(title, "CHL", gameDate, gameDate.ToDateTime(new TimeOnly(19, 0)));

    private static GameDayPoll ExistingPoll(DateOnly gameDate, string title) => new()
    {
        PollId = Guid.NewGuid().ToString("N"),
        SeasonId = Season.DeriveSeasonId(gameDate),
        GameDate = gameDate,
        Title = title,
        Deadline = gameDate.ToDateTime(new TimeOnly(19, 0)),
        CreatedBy = "admin-user"
    };

    private static (FixtureImportService Service, IIcsFixtureFeedFetcher Fetcher, IPollRepository PollRepository, ISeasonRepository SeasonRepository)
        CreateService(FixtureCandidate[] fixtures, GameDayPoll[]? existingPolls = null)
    {
        var fetcher = Substitute.For<IIcsFixtureFeedFetcher>();
        fetcher.FetchAsync(IcsUrl, Arg.Any<CancellationToken>()).Returns(fixtures.ToList());

        var pollRepository = Substitute.For<IPollRepository>();
        var seasonRepository = Substitute.For<ISeasonRepository>();

        // A season only "exists" (and so can be checked for existing polls) once something has
        // been created in it - matches EnsureSeasonExistsAsync's own create-on-first-poll behaviour.
        var seasonsWithPolls = (existingPolls ?? [])
            .Select(p => p.SeasonId)
            .Distinct()
            .ToList();

        foreach (var seasonId in seasonsWithPolls)
        {
            var gameDate = (existingPolls ?? []).First(p => p.SeasonId == seasonId).GameDate;
            seasonRepository.GetByIdAsync(seasonId, Arg.Any<CancellationToken>())
                .Returns(Season.CreateForDate(gameDate));

            pollRepository.ListBySeasonAsync(seasonId, Arg.Any<CancellationToken>())
                .Returns((existingPolls ?? []).Where(p => p.SeasonId == seasonId).ToList());
        }

        return (new FixtureImportService(fetcher, pollRepository, seasonRepository), fetcher, pollRepository, seasonRepository);
    }

    // ─── Date filtering ─────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_FiltersOutFixturesBeforeStartDate()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var (service, _, _, _) = CreateService([
            Fixture(startDate.AddDays(-1)),
            Fixture(startDate),
            Fixture(startDate.AddDays(1))
        ]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.True(f.GameDate >= startDate));
    }

    [Fact]
    public async Task PreviewAsync_NoEndDate_IncludesEverythingFromStartDateOnward()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var (service, _, _, _) = CreateService([Fixture(startDate.AddYears(1))]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    [Fact]
    public async Task PreviewAsync_EndDateGiven_ExcludesFixturesAfterIt()
    {
        var startDate = new DateOnly(2026, 1, 15);
        var endDate = new DateOnly(2026, 1, 31);
        var (service, _, _, _) = CreateService([
            Fixture(new DateOnly(2026, 1, 20)),
            Fixture(new DateOnly(2026, 2, 1)) // spans into the next month - excluded
        ]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 1, 20), result[0].GameDate);
    }

    [Fact]
    public async Task PreviewAsync_OrdersByDeadline()
    {
        var startDate = new DateOnly(2026, 1, 1);
        var (service, _, _, _) = CreateService([
            Fixture(new DateOnly(2026, 1, 20), "Later"),
            Fixture(new DateOnly(2026, 1, 10), "Earlier")
        ]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.Equal(["Earlier", "Later"], result.Select(f => f.Title));
    }

    // ─── Already-imported detection ─────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_NoExistingPolls_NothingIsFlaggedAlreadyImported()
    {
        var startDate = new DateOnly(2026, 1, 1);
        var (service, _, _, _) = CreateService([Fixture(new DateOnly(2026, 1, 20))]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.All(result, f => Assert.False(f.AlreadyImported));
    }

    [Fact]
    public async Task PreviewAsync_MatchingTitleAndGameDate_IsFlaggedAlreadyImported()
    {
        var gameDate = new DateOnly(2026, 1, 20);
        var startDate = new DateOnly(2026, 1, 1);
        var (service, _, _, _) = CreateService(
            [Fixture(gameDate, "Belfast Giants vs Sheffield Steelers")],
            [ExistingPoll(gameDate, "Belfast Giants vs Sheffield Steelers")]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result).AlreadyImported);
    }

    [Fact]
    public async Task PreviewAsync_SameTitleDifferentGameDate_IsNotFlagged()
    {
        var startDate = new DateOnly(2026, 1, 1);
        var (service, _, _, _) = CreateService(
            [Fixture(new DateOnly(2026, 1, 20), "Belfast Giants vs Sheffield Steelers")],
            [ExistingPoll(new DateOnly(2026, 1, 27), "Belfast Giants vs Sheffield Steelers")]);

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(result).AlreadyImported);
    }

    [Fact]
    public async Task PreviewAsync_SeasonNeverCreated_SkipsTheLookup_RatherThanCreatingIt()
    {
        var startDate = new DateOnly(2026, 1, 1);
        var (service, _, pollRepository, seasonRepository) = CreateService([Fixture(new DateOnly(2026, 1, 20))]);
        // No existing polls registered anywhere - GetByIdAsync/ListBySeasonAsync default to null/empty.

        var result = await service.PreviewAsync(IcsUrl, startDate, endDate: null, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(result).AlreadyImported);
        await seasonRepository.DidNotReceive().SaveAsync(Arg.Any<Season>(), Arg.Any<CancellationToken>());
    }
}
