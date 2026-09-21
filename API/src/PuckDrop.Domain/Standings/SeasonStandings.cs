using PuckDrop.Domain.Entities;

namespace PuckDrop.Domain.Standings;

/// <summary>
/// Folds a season's scoring facts into leaderboard standings.
/// </summary>
/// <remarks>
/// The single place a season total is ever derived. Pure - no I/O, no ordering requirements on its
/// inputs - which is what makes the awkward cases (a void recorded before its poll was scored, a
/// poll re-scored while voided, a deduction taking someone below zero) fall out rather than needing
/// to be handled. Nothing may incrementally adjust a total elsewhere: if a write path ever "just
/// adds" the new poll's points, order-independence dies silently.
/// </remarks>
public static class SeasonStandings
{
    /// <summary>
    /// Builds the standings for a season, ordered by effective points descending.
    /// </summary>
    /// <remarks>
    /// Ties break on user ID, matching the byte order DynamoDB returned when the score was part of
    /// the sort key, so an existing leaderboard doesn't reshuffle. Ranking itself is applied by
    /// the application layer.
    /// </remarks>
    public static IReadOnlyList<LeaderboardEntry> Build(
        string seasonId,
        IEnumerable<PollScore> scores,
        IEnumerable<PollVoid> voids,
        IEnumerable<PointAdjustment> adjustments)
    {
        var scoresByUser = scores.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var voidsByUser = voids.GroupBy(v => v.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var adjustmentsByUser = adjustments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.ToList());

        // Rows come from scores and adjustments only. A void on its own means a poll that hasn't
        // been scored yet, which is nothing to show; the void still applies once it is scored.
        var userIds = scoresByUser.Keys.Concat(adjustmentsByUser.Keys).Distinct();

        var entries = new List<LeaderboardEntry>();

        foreach (var userId in userIds)
        {
            var userScores = scoresByUser.GetValueOrDefault(userId) ?? [];
            var userVoids = voidsByUser.GetValueOrDefault(userId) ?? [];
            var userAdjustments = adjustmentsByUser.GetValueOrDefault(userId) ?? [];

            var voidedPollIds = userVoids.Select(v => v.PollId).ToHashSet();
            var counted = userScores.Where(s => !voidedPollIds.Contains(s.PollId)).ToList();

            // Only surface voids that actually suppressed a score, so the reasons shown next to a
            // player's name always explain points they can see are missing.
            var appliedVoids = userVoids
                .Where(v => userScores.Any(s => s.PollId == v.PollId))
                .OrderBy(v => v.VoidedAt)
                .ToList();

            var latestScore = userScores.OrderByDescending(s => s.ScoredAt).FirstOrDefault();
            var orderedAdjustments = userAdjustments.OrderBy(a => a.CreatedAt).ToList();

            var timestamps = counted.Select(s => s.ScoredAt)
                .Concat(orderedAdjustments.Select(a => a.CreatedAt))
                .Concat(appliedVoids.Select(v => v.VoidedAt))
                .ToList();

            entries.Add(new LeaderboardEntry
            {
                UserId = userId,
                SeasonId = seasonId,
                // Prefer the most recently scored poll's name, so a name captured wrongly - or since
                // changed - self-heals. Falls back to an adjustment for a player who has one but no
                // scores yet, because a name is only ever captured from a player's own answers.
                DisplayName = latestScore?.DisplayName
                    ?? orderedAdjustments[^1].DisplayName,
                EarnedPoints = counted.Sum(s => s.Points),
                AdjustmentPoints = orderedAdjustments.Sum(a => a.Points),
                TotalAnswered = counted.Sum(s => s.Answered),
                Adjustments = orderedAdjustments,
                Voids = appliedVoids,
                LastUpdated = timestamps.Count > 0 ? timestamps.Max() : DateTime.UtcNow
            });
        }

        return entries
            .OrderByDescending(e => e.TotalPoints)
            .ThenBy(e => e.UserId, StringComparer.Ordinal)
            .ToList();
    }
}
