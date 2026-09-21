using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Mappings;

public static class LeaderboardMappingExtensions
{
    public static LeaderboardEntryResponse ToResponse(this RankedEntry entry) => new(
        entry.Entry.UserId, entry.Entry.DisplayName,
        entry.Entry.TotalPoints, entry.Entry.TotalAnswered, entry.Rank,
        entry.Entry.EarnedPoints, entry.Entry.AdjustmentPoints,
        // Reasons go out to every player, not just admins - a penalty nobody can see the reason
        // for is what starts the argument. Who applied it stays internal.
        entry.Entry.Adjustments
            .Select(a => new LeaderboardAdjustmentResponse(a.AdjustmentId, a.Points, a.Reason)).ToList(),
        entry.Entry.Voids
            .Select(v => new LeaderboardVoidResponse(v.PollId, v.PollTitle, v.Reason)).ToList());

    public static LeaderboardResponse ToResponse(this LeaderboardResult result) => new(
        result.SeasonId, result.Entries.Select(e => e.ToResponse()).ToList());
}
