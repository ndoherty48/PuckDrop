using PuckDrop.Domain.Entities;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class SeasonService(ISeasonRepository seasonRepository)
{
    /// <summary>
    /// Gets the current active season based on today's date.
    /// Returns null if no season exists yet for the current period.
    /// </summary>
    public async Task<Season?> GetCurrentSeasonAsync(CancellationToken cancellationToken = default)
    {
        var seasonId = Season.DeriveSeasonId(DateOnly.FromDateTime(DateTime.UtcNow));
        return await seasonRepository.GetByIdAsync(seasonId, cancellationToken);
    }

    /// <summary>
    /// Lists all seasons.
    /// </summary>
    public async Task<IReadOnlyList<Season>> ListSeasonsAsync(CancellationToken cancellationToken = default)
    {
        return await seasonRepository.ListAllAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures a season exists for the given game date.
    /// Creates one automatically if it doesn't exist (per domain rule: auto-created on first poll).
    /// Returns the season.
    /// </summary>
    public async Task<Season> EnsureSeasonExistsAsync(DateOnly gameDate, CancellationToken cancellationToken = default)
    {
        var seasonId = Season.DeriveSeasonId(gameDate);
        var existing = await seasonRepository.GetByIdAsync(seasonId, cancellationToken);

        if (existing is not null)
            return existing;

        var season = Season.CreateForDate(gameDate);
        await seasonRepository.SaveAsync(season, cancellationToken);
        return season;
    }
}
