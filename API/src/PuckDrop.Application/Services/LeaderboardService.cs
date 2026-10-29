using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Standings;
using PuckDrop.Application.Repositories;

namespace PuckDrop.Application.Services;

public class LeaderboardService(ILeaderboardRepository leaderboardRepository, SeasonService seasonService)
{
    /// <summary>
    /// Gets the leaderboard for a season. Defaults to the current season if seasonId is null.
    /// Entries come back ranked, tie-break applied - see <see cref="SeasonStandings.Build"/>.
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

        return new LeaderboardResult(seasonId, entries);
    }
}

public record LeaderboardResult(string SeasonId, IReadOnlyList<LeaderboardEntry> Entries);

