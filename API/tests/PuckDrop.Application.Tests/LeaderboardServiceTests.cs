using NSubstitute;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class LeaderboardServiceTests
{
    private static LeaderboardEntry Entry(string userId, int totalPoints) => new()
    {
        UserId = userId,
        SeasonId = "2025-26",
        DisplayName = userId,
        TotalPoints = totalPoints,
        TotalAnswered = totalPoints // irrelevant to ranking, just needs to be consistent
    };

    private static (LeaderboardService Service, ILeaderboardRepository LeaderboardRepository) CreateService(
        Season? currentSeason, IReadOnlyList<LeaderboardEntry> entries)
    {
        var leaderboardRepository = Substitute.For<ILeaderboardRepository>();
        leaderboardRepository.GetLeaderboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(entries);

        var seasonRepository = Substitute.For<ISeasonRepository>();
        if (currentSeason is not null)
            seasonRepository.GetByIdAsync(currentSeason.SeasonId, Arg.Any<CancellationToken>()).Returns(currentSeason);

        var seasonService = new SeasonService(seasonRepository);
        return (new LeaderboardService(leaderboardRepository, seasonService), leaderboardRepository);
    }

    [Fact]
    public async Task GetLeaderboardAsync_MixedTies_UsesCompetitionRanking()
    {
        // [1, 2, 2, 4] - the tied pair shares rank 2, and the next entry jumps to 4 (its
        // position), not 3 (dense ranking would give 3 - this must NOT be dense ranking).
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, [Entry("a", 100), Entry("b", 80), Entry("c", 80), Entry("d", 50)]);

        var result = await service.GetLeaderboardAsync(season.SeasonId);

        Assert.Equal([1, 2, 2, 4], result.Entries.Select(e => e.Rank));
    }

    [Fact]
    public async Task GetLeaderboardAsync_AllTied_EveryoneRanksFirst()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, [Entry("a", 50), Entry("b", 50), Entry("c", 50)]);

        var result = await service.GetLeaderboardAsync(season.SeasonId);

        Assert.Equal([1, 1, 1], result.Entries.Select(e => e.Rank));
    }

    [Fact]
    public async Task GetLeaderboardAsync_NoTies_RanksSequentially()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, [Entry("a", 30), Entry("b", 20), Entry("c", 10)]);

        var result = await service.GetLeaderboardAsync(season.SeasonId);

        Assert.Equal([1, 2, 3], result.Entries.Select(e => e.Rank));
    }

    [Fact]
    public async Task GetLeaderboardAsync_EmptyLeaderboard_ReturnsEmptyEntries()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, []);

        var result = await service.GetLeaderboardAsync(season.SeasonId);

        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task GetLeaderboardAsync_NoSeasonIdAndNoCurrentSeason_ReturnsEmptyResultWithDerivedSeasonId()
    {
        var (service, leaderboardRepository) = CreateService(currentSeason: null, entries: []);

        var result = await service.GetLeaderboardAsync(seasonId: null);

        Assert.Empty(result.Entries);
        Assert.Equal(Season.DeriveSeasonId(DateOnly.FromDateTime(DateTime.UtcNow)), result.SeasonId);
        // The empty-result fallback must short-circuit before ever querying the leaderboard
        // repository for a season that (by construction) doesn't exist.
        await leaderboardRepository.DidNotReceive().GetLeaderboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLeaderboardAsync_NoSeasonIdWithCurrentSeason_ResolvesCurrentSeasonId()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, leaderboardRepository) = CreateService(season, [Entry("a", 10)]);

        var result = await service.GetLeaderboardAsync(seasonId: null);

        Assert.Equal(season.SeasonId, result.SeasonId);
        await leaderboardRepository.Received(1).GetLeaderboardAsync(season.SeasonId, Arg.Any<CancellationToken>());
    }
}
