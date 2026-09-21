using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class PointAdjustmentServiceTests
{
    private const string SeasonId = "2025-26";
    private const string UserId = "user-1";
    private const string AdminId = "admin-user";

    private sealed class Fixture
    {
        public required PointAdjustmentService Service { get; init; }
        public required ILeaderboardRepository LeaderboardRepository { get; init; }
    }

    private static Fixture CreateFixture(bool userHasStanding = true, Season? currentSeason = null)
    {
        var leaderboardRepository = Substitute.For<ILeaderboardRepository>();
        leaderboardRepository.GetSeasonFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SeasonFacts(
                userHasStanding
                    ? [new PollScore
                        {
                            SeasonId = SeasonId, PollId = "poll-1", UserId = UserId,
                            DisplayName = "Nathan", Points = 3, Answered = 4
                        }]
                    : [],
                [], []));

        var seasonRepository = Substitute.For<ISeasonRepository>();
        if (currentSeason is not null)
            seasonRepository.GetByIdAsync(currentSeason.SeasonId, Arg.Any<CancellationToken>()).Returns(currentSeason);

        return new Fixture
        {
            Service = new PointAdjustmentService(leaderboardRepository, new SeasonService(seasonRepository)),
            LeaderboardRepository = leaderboardRepository
        };
    }

    // ─── Adding ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_CopiesTheDisplayNameFromThePlayersStanding()
    {
        // There is no user directory - a name is only ever captured from a player's own answers -
        // so the adjustment has to carry its own copy or the leaderboard row would be nameless.
        var fixture = CreateFixture();

        var adjustment = await fixture.Service.AddAsync(
            SeasonId, UserId, -5, "Picked after puck drop", AdminId, TestContext.Current.CancellationToken);

        Assert.Equal("Nathan", adjustment.DisplayName);
        Assert.Equal(-5, adjustment.Points);
        Assert.Equal("Picked after puck drop", adjustment.Reason);
        Assert.Equal(AdminId, adjustment.CreatedBy);
        await fixture.LeaderboardRepository.Received(1)
            .SaveAdjustmentAsync(Arg.Is<PointAdjustment>(a => a.UserId == UserId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_GivesEachAdjustmentItsOwnId()
    {
        var fixture = CreateFixture();

        var first = await fixture.Service.AddAsync(SeasonId, UserId, -1, "One", AdminId, TestContext.Current.CancellationToken);
        var second = await fixture.Service.AddAsync(SeasonId, UserId, -1, "Two", AdminId, TestContext.Current.CancellationToken);

        Assert.NotEqual(first.AdjustmentId, second.AdjustmentId);
    }

    [Fact]
    public async Task AddAsync_PlayerHasNoStandingYet_Throws()
    {
        var fixture = CreateFixture(userHasStanding: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.AddAsync(
            SeasonId, UserId, -5, "No-show", AdminId, TestContext.Current.CancellationToken));

        await fixture.LeaderboardRepository.DidNotReceive()
            .SaveAdjustmentAsync(Arg.Any<PointAdjustment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ZeroPoints_Throws()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.AddAsync(
            SeasonId, UserId, 0, "Nothing", AdminId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsync_BlankReason_Throws()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AddAsync(
            SeasonId, UserId, -5, "  ", AdminId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsync_NoSeasonIdAndNoCurrentSeason_Throws()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AddAsync(
            null, UserId, -5, "No-show", AdminId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddAsync_NoSeasonIdButACurrentSeason_UsesIt()
    {
        var season = Season.CreateForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var fixture = CreateFixture(currentSeason: season);

        var adjustment = await fixture.Service.AddAsync(
            null, UserId, -5, "No-show", AdminId, TestContext.Current.CancellationToken);

        Assert.Equal(season.SeasonId, adjustment.SeasonId);
    }

    // ─── Removing ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_DeletesByAllThreeKeyParts()
    {
        // The adjustment is stored under season and user as well as its id, and there is no index
        // on the id alone.
        var fixture = CreateFixture();

        await fixture.Service.RemoveAsync(SeasonId, UserId, "adj-1", TestContext.Current.CancellationToken);

        await fixture.LeaderboardRepository.Received(1)
            .DeleteAdjustmentAsync(SeasonId, UserId, "adj-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_NoSuchAdjustment_PropagatesNotFound()
    {
        var fixture = CreateFixture();
        fixture.LeaderboardRepository
            .DeleteAdjustmentAsync(SeasonId, UserId, "nope", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new KeyNotFoundException("Adjustment not found.")));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.RemoveAsync(SeasonId, UserId, "nope", TestContext.Current.CancellationToken));
    }
}
