using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Mappings;

public static class LeaderboardMappingExtensions
{
    public static LeaderboardEntryResponse ToResponse(this RankedEntry entry) => new(
        entry.Entry.UserId, entry.Entry.DisplayName,
        entry.Entry.TotalPoints, entry.Entry.TotalAnswered, entry.Rank);

    public static LeaderboardResponse ToResponse(this LeaderboardResult result) => new(
        result.SeasonId, result.Entries.Select(e => e.ToResponse()).ToList());
}
