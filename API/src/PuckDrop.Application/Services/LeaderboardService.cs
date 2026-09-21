using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Standings;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class LeaderboardService(ILeaderboardRepository leaderboardRepository, SeasonService seasonService)
{
    /// <summary>
    /// Gets the leaderboard for a season. Defaults to the current season if seasonId is null.
    /// Entries are returned sorted by points descending. Rank is calculated with shared ranks for ties.
    /// </summary>
    public async Task<LeaderboardResult> GetLeaderboardAsync(string? seasonId = null, CancellationToken cancellationToken = default)
    {
        if (seasonId is null)
        {
            var currentSeason = await seasonService.GetCurrentSeasonAsync(cancellationToken);
            if (currentSeason is null)
                return new LeaderboardResult(Season.DeriveSeasonId(DateOnly.FromDateTime(DateTime.UtcNow)), []);

            seasonId = currentSeason.SeasonId;
        }

        var facts = await leaderboardRepository.GetSeasonFactsAsync(seasonId, cancellationToken);
        var entries = SeasonStandings.Build(seasonId, facts.Scores, facts.Voids, facts.Adjustments);
        var ranked = AssignRanks(entries);

        return new LeaderboardResult(seasonId, ranked);
    }

    /// <summary>
    /// Assigns ranks with shared ranks for ties (1, 2, 2, 4 pattern).
    /// </summary>
    private static IReadOnlyList<RankedEntry> AssignRanks(IReadOnlyList<LeaderboardEntry> entries)
    {
        var ranked = new List<RankedEntry>();
        int currentRank = 0;
        // Nullable, not a -1 sentinel: deductions can take a real total negative, and a player on
        // exactly -1 point would otherwise be treated as tied with the initial value and ranked 0.
        int? previousPoints = null;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.TotalPoints != previousPoints)
            {
                currentRank = i + 1;
                previousPoints = entry.TotalPoints;
            }

            ranked.Add(new RankedEntry(entry, currentRank));
        }

        return ranked;
    }
}

public record LeaderboardResult(string SeasonId, IReadOnlyList<RankedEntry> Entries);

public record RankedEntry(LeaderboardEntry Entry, int Rank);
