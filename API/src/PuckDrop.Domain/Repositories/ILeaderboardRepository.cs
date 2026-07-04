using PuckDrop.Domain.Entities;

namespace PuckDrop.Domain.Repositories;

public interface ILeaderboardRepository
{
    /// <summary>
    /// Gets the leaderboard entries for a season, sorted by points descending.
    /// </summary>
    Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(string seasonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single user's leaderboard entry for a season.
    /// </summary>
    Task<LeaderboardEntry?> GetEntryAsync(string seasonId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a leaderboard entry. Handles the inverted sort key for ordering.
    /// </summary>
    Task SaveEntryAsync(LeaderboardEntry entry, int? previousPoints = null, CancellationToken cancellationToken = default);
}
