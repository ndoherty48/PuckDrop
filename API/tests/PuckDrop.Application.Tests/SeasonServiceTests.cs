using NSubstitute;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class SeasonServiceTests
{
    [Fact]
    public async Task EnsureSeasonExistsAsync_SeasonAlreadyExists_ReturnsExistingWithoutSaving()
    {
        var existing = Season.CreateForDate(new DateOnly(2026, 1, 15));
        var repository = Substitute.For<ISeasonRepository>();
        repository.GetByIdAsync(existing.SeasonId, Arg.Any<CancellationToken>())
            .Returns(existing);
        var service = new SeasonService(repository);

        var result = await service.EnsureSeasonExistsAsync(new DateOnly(2026, 1, 15), TestContext.Current.CancellationToken);

        Assert.Same(existing, result);
        await repository.DidNotReceive().SaveAsync(Arg.Any<Season>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureSeasonExistsAsync_SeasonMissing_CreatesAndSavesNewSeason()
    {
        var repository = Substitute.For<ISeasonRepository>();
        repository.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Season?)null);
        var service = new SeasonService(repository);
        var gameDate = new DateOnly(2026, 1, 15);

        var result = await service.EnsureSeasonExistsAsync(gameDate, TestContext.Current.CancellationToken);

        Assert.Equal(Season.DeriveSeasonId(gameDate), result.SeasonId);
        await repository.Received(1).SaveAsync(
            Arg.Is<Season>(s => s.SeasonId == result.SeasonId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentSeasonAsync_NoSeasonForToday_ReturnsNull()
    {
        var repository = Substitute.For<ISeasonRepository>();
        repository.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Season?)null);
        var service = new SeasonService(repository);

        var result = await service.GetCurrentSeasonAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
