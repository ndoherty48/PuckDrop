using PuckDrop.Application.Repositories;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Standings;

namespace PuckDrop.Application.Services;

/// <summary>
/// Applies and removes manual point adjustments on a player's season total.
/// </summary>
/// <remarks>
/// An adjustment is its own fact, so removing one restores the total exactly - there is no delta to
/// unwind. Negative adjustments may take a total below zero; that is deliberate.
/// </remarks>
public class PointAdjustmentService(ILeaderboardRepository leaderboardRepository, SeasonService seasonService)
{
    /// <summary>
    /// Applies a signed points adjustment to a player who is already on the leaderboard.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no season could be resolved.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the player has no leaderboard standing yet.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if points is zero or beyond the bound.</exception>
    /// <exception cref="ArgumentException">Thrown if the reason is blank or too long.</exception>
    public async Task<PointAdjustment> AddAsync(
        string? seasonId, string userId, int points, string reason, string createdBy,
        CancellationToken cancellationToken = default)
    {
        var season = await ResolveSeasonIdAsync(seasonId, cancellationToken);

        // The display name has to be copied from the player's standing: names are only ever captured
        // from a player's own answers, and there is no user directory to look one up in. That also
        // means an adjustment can only target someone who has already appeared in a scored poll.
        var facts = await leaderboardRepository.GetSeasonFactsAsync(season, cancellationToken);
        var standing = SeasonStandings.Build(season, facts.Scores, facts.Voids, facts.Adjustments)
            .FirstOrDefault(e => e.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"User '{userId}' has no standing in season '{season}' to adjust. They need to have "
                + "answered a scored poll first.");

        var adjustment = PointAdjustment.Create(
            season, Guid.CreateVersion7().ToString("N"), userId, standing.DisplayName, points, reason, createdBy);

        await leaderboardRepository.SaveAdjustmentAsync(adjustment, cancellationToken);
        return adjustment;
    }

    /// <summary>
    /// Removes an adjustment, restoring the player's total to exactly what it was.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if no such adjustment exists.</exception>
    public async Task RemoveAsync(
        string? seasonId, string userId, string adjustmentId, CancellationToken cancellationToken = default)
    {
        var season = await ResolveSeasonIdAsync(seasonId, cancellationToken);
        await leaderboardRepository.DeleteAdjustmentAsync(season, userId, adjustmentId, cancellationToken);
    }

    private async Task<string> ResolveSeasonIdAsync(string? seasonId, CancellationToken cancellationToken)
    {
        if (seasonId is not null)
            return seasonId;

        var currentSeason = await seasonService.GetCurrentSeasonAsync(cancellationToken)
            ?? throw new InvalidOperationException("No current season to adjust points in.");

        return currentSeason.SeasonId;
    }
}
