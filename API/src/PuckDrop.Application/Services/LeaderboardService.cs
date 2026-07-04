using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Application.Services;

public class LeaderboardService
{
    private readonly ILeaderboardRepository _leaderboardRepository;
    private readonly SeasonService _seasonService;

    public LeaderboardService(ILeaderboardRepository leaderboardRepository, SeasonService seasonService)
    {
        _leaderboardRepository = leaderboardRepository;
        _seasonService = seasonService;
    }

    /// <summary>
    /// Gets the leaderboard for a season. Defaults to the current season if seasonId is null.
    /// Entries are returned sorted by points descending. Rank is calculated with shared ranks for ties.
    /// </summary>
    public async Task<LeaderboardResult> GetLeaderboardAsync(string? seasonId = null, CancellationToken cancellationToken = default)
    {
        if (seasonId is null)
        {
            var currentSeason = await _seasonService.GetCurrentSeasonAsync(cancellationToken);
            if (currentSeason is null)
                return new LeaderboardResult(Season.DeriveSeasonId(DateOnly.FromDateTime(DateTime.UtcNow)), []);

            seasonId = currentSeason.SeasonId;
        }

        var entries = await _leaderboardRepository.GetLeaderboardAsync(seasonId, cancellationToken);
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
        int previousPoints = -1;

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
