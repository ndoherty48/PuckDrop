using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class LeaderboardServiceTests
{
    private const string SeasonId = "2025-26";

    /// <summary>
    /// One scored poll's worth of points for a player. The service folds these into standings, so
    /// tests can hand them over in any order - ordering is the service's job, not the fixture's.
    /// </summary>
    private static PollScore Score(string userId, int points) => new()
    {
        SeasonId = SeasonId,
        PollId = "poll-1",
        UserId = userId,
        DisplayName = userId,
        Points = points,
        Answered = points // irrelevant to ranking, just needs to be consistent
    };

    private static (LeaderboardService Service, ILeaderboardRepository LeaderboardRepository) CreateService(
        Season? currentSeason, IReadOnlyList<PollScore> scores, IReadOnlyList<PointAdjustment>? adjustments = null)
    {
        var leaderboardRepository = Substitute.For<ILeaderboardRepository>();
        leaderboardRepository.GetSeasonFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SeasonFacts(scores, [], adjustments ?? []));

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
        // Deliberately out of order: the service folds and sorts, so a sorted fixture would
        // hide a sorting bug.
        var (service, _) = CreateService(season, [Score("c", 80), Score("d", 50), Score("a", 100), Score("b", 80)]);

        var result = await service.GetLeaderboardAsync(season.SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("a", 1), ("b", 2), ("c", 2), ("d", 4)],
            result.Entries.Select(e => (e.Entry.UserId, e.Rank)));
    }

    [Fact]
    public async Task GetLeaderboardAsync_AllTied_EveryoneRanksFirst()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, [Score("a", 50), Score("b", 50), Score("c", 50)]);

        var result = await service.GetLeaderboardAsync(season.SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("a", 1), ("b", 1), ("c", 1)],
            result.Entries.Select(e => (e.Entry.UserId, e.Rank)));
    }

    [Fact]
    public async Task GetLeaderboardAsync_NoTies_RanksSequentially()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, [Score("a", 30), Score("b", 20), Score("c", 10)]);

        var result = await service.GetLeaderboardAsync(season.SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("a", 1), ("b", 2), ("c", 3)],
            result.Entries.Select(e => (e.Entry.UserId, e.Rank)));
    }

    [Fact]
    public async Task GetLeaderboardAsync_EmptyLeaderboard_ReturnsEmptyEntries()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, _) = CreateService(season, []);

        var result = await service.GetLeaderboardAsync(season.SeasonId, TestContext.Current.CancellationToken);

        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task GetLeaderboardAsync_NoSeasonIdAndNoCurrentSeason_ReturnsEmptyResultWithDerivedSeasonId()
    {
        var (service, leaderboardRepository) = CreateService(currentSeason: null, scores: []);

        var result = await service.GetLeaderboardAsync(null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Entries);
        Assert.Equal(Season.DeriveSeasonId(DateOnly.FromDateTime(DateTime.UtcNow)), result.SeasonId);
        // The empty-result fallback must short-circuit before ever querying the leaderboard
        // repository for a season that (by construction) doesn't exist.
        await leaderboardRepository.DidNotReceive().GetSeasonFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLeaderboardAsync_NoSeasonIdWithCurrentSeason_ResolvesCurrentSeasonId()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var (service, leaderboardRepository) = CreateService(season, [Score("a", 10)]);

        var result = await service.GetLeaderboardAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(season.SeasonId, result.SeasonId);
        await leaderboardRepository.Received(1).GetSeasonFactsAsync(season.SeasonId, Arg.Any<CancellationToken>());
    }
}
