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
    /// Builds the ranked standings for a season.
    /// </summary>
    /// <remarks>
    /// Ordered by effective points, then the tie-break: most game days played, then head-to-head
    /// among the players still level. Anyone the tie-break can't separate shares a rank (1, 2, 2,
    /// 4), and is listed by user ID within it so the order is stable.
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
        var gameDaysByUser = new Dictionary<string, IReadOnlyDictionary<string, int>>();

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

            // A game day with nothing graded wasn't played: there was nothing to get right or wrong.
            var gameDays = counted.Where(s => s.Answered > 0).ToDictionary(s => s.PollId, s => s.Points);
            gameDaysByUser[userId] = gameDays;

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
                GameDaysPlayed = gameDays.Count,
                Adjustments = orderedAdjustments,
                Voids = appliedVoids,
                LastUpdated = timestamps.Count > 0 ? timestamps.Max() : DateTime.UtcNow
            });
        }

        return Rank(entries, gameDaysByUser);
    }

    /// <summary>
    /// Orders the entries, applies the tie-break and assigns ranks.
    /// </summary>
    /// <remarks>
    /// Head-to-head only exists relative to the players level with you, and it can be circular (A
    /// beats B, B beats C, C beats A), so it is not a per-pair comparer handed to a sort - that
    /// would break the sort. Each group still level on points and game days played instead gets one
    /// mini-league, scored once; its net record is then just another sort key. No second pass: if
    /// the mini-league leaves some still level, they share a rank.
    /// </remarks>
    private static List<LeaderboardEntry> Rank(
        List<LeaderboardEntry> entries, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> gameDaysByUser)
    {
        var headToHead = new Dictionary<string, HeadToHeadRecord>();

        foreach (var group in entries.GroupBy(e => (e.TotalPoints, e.GameDaysPlayed)).Where(g => g.Count() > 1))
        {
            foreach (var entry in group)
            {
                headToHead[entry.UserId] = group
                    .Where(rival => rival.UserId != entry.UserId)
                    .Select(rival => HeadToHead(gameDaysByUser[entry.UserId], gameDaysByUser[rival.UserId]))
                    .Aggregate(HeadToHeadRecord.None, (total, record) => total.Plus(record));
            }
        }

        int Net(LeaderboardEntry e) => headToHead.GetValueOrDefault(e.UserId)?.Net ?? 0;

        var ordered = entries
            .OrderByDescending(e => e.TotalPoints)
            .ThenByDescending(e => e.GameDaysPlayed)
            .ThenByDescending(Net)
            .ThenBy(e => e.UserId, StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];
            var previous = i > 0 ? ordered[i - 1] : null;

            entry.Rank = previous is not null
                && previous.TotalPoints == entry.TotalPoints
                && previous.GameDaysPlayed == entry.GameDaysPlayed
                && Net(previous) == Net(entry)
                    ? previous.Rank
                    : i + 1;
        }

        foreach (var entry in ordered)
            entry.IsRankShared = ordered.Any(e => e != entry && e.Rank == entry.Rank);

        return ordered;
    }

    /// <summary>
    /// One player's record against another over the game days they both played. Earned points
    /// only - an adjustment isn't tied to a game day, so it can't take or lose one.
    /// </summary>
    private static HeadToHeadRecord HeadToHead(
        IReadOnlyDictionary<string, int> player, IReadOnlyDictionary<string, int> rival)
    {
        int won = 0, lost = 0, drawn = 0;

        foreach (var (pollId, points) in player)
        {
            if (!rival.TryGetValue(pollId, out var rivalPoints))
                continue;

            if (points > rivalPoints) won++;
            else if (points < rivalPoints) lost++;
            else drawn++;
        }

        return new HeadToHeadRecord(won, lost, drawn);
    }

    /// <summary>
    /// Game days taken, lost and drawn against other players, over only the game days each pair
    /// both played.
    /// </summary>
    private sealed record HeadToHeadRecord(int Won, int Lost, int Drawn)
    {
        public static readonly HeadToHeadRecord None = new(0, 0, 0);

        /// <summary>
        /// What the mini-league ranks on. Net rather than raw wins, so a player who simply shared
        /// more game days with the others isn't ahead for that alone.
        /// </summary>
        public int Net => Won - Lost;

        public HeadToHeadRecord Plus(HeadToHeadRecord other) =>
            new(Won + other.Won, Lost + other.Lost, Drawn + other.Drawn);
    }
}
