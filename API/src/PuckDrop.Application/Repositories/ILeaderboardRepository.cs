using PuckDrop.Application.Models;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Application.Repositories;

/// <summary>
/// Stores the scoring facts a season's standings are folded from.
/// </summary>
/// <remarks>
/// There is no stored leaderboard total. Every write here records a fact - what a player earned in
/// a poll, a voided game day, a manual adjustment - and totals are derived on read. That is what
/// makes re-scoring, voiding and deducting points safe to apply and to undo.
/// </remarks>
public interface ILeaderboardRepository
{
    /// <summary>
    /// Gets every scoring fact recorded for a season, in one query.
    /// </summary>
    Task<SeasonFacts> GetSeasonFactsAsync(string seasonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what each player earned in a poll, replacing anything previously recorded for the
    /// same player and poll. Re-scoring therefore overwrites rather than accumulating.
    /// </summary>
    Task SavePollScoresAsync(IReadOnlyList<PollScore> scores, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a voided game day for one player, replacing any existing void for the same pair.
    /// </summary>
    Task SaveVoidAsync(PollVoid pollVoid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a void, restoring the player's points for that poll.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if no such void exists.</exception>
    Task DeleteVoidAsync(string seasonId, string userId, string pollId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a manual point adjustment.
    /// </summary>
    Task SaveAdjustmentAsync(PointAdjustment adjustment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an adjustment, restoring the player's total exactly.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if no such adjustment exists.</exception>
    Task DeleteAdjustmentAsync(string seasonId, string userId, string adjustmentId, CancellationToken cancellationToken = default);
}
