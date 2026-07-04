using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Application.Services;

public class SeasonService
{
    private readonly ISeasonRepository _seasonRepository;

    public SeasonService(ISeasonRepository seasonRepository)
    {
        _seasonRepository = seasonRepository;
    }

    /// <summary>
    /// Gets the current active season based on today's date.
    /// Returns null if no season exists yet for the current period.
    /// </summary>
    public async Task<Season?> GetCurrentSeasonAsync(CancellationToken cancellationToken = default)
    {
        var seasonId = Season.DeriveSeasonId(DateOnly.FromDateTime(DateTime.UtcNow));
        return await _seasonRepository.GetByIdAsync(seasonId, cancellationToken);
    }

    /// <summary>
    /// Lists all seasons.
    /// </summary>
    public async Task<IReadOnlyList<Season>> ListSeasonsAsync(CancellationToken cancellationToken = default)
    {
        return await _seasonRepository.ListAllAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures a season exists for the given game date.
    /// Creates one automatically if it doesn't exist (per domain rule: auto-created on first poll).
    /// Returns the season.
    /// </summary>
    public async Task<Season> EnsureSeasonExistsAsync(DateOnly gameDate, CancellationToken cancellationToken = default)
    {
        var seasonId = Season.DeriveSeasonId(gameDate);
        var existing = await _seasonRepository.GetByIdAsync(seasonId, cancellationToken);

        if (existing is not null)
            return existing;

        var season = Season.CreateForDate(gameDate);
        await _seasonRepository.SaveAsync(season, cancellationToken);
        return season;
    }
}
