using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Standings;

namespace PuckDrop.Api.Mappings;

public static class LeaderboardMappingExtensions
{
    public static LeaderboardEntryResponse ToResponse(this LeaderboardEntry entry) => new(
        entry.UserId, entry.DisplayName,
        entry.TotalPoints, entry.TotalAnswered, entry.Rank,
        entry.EarnedPoints, entry.AdjustmentPoints,
        // Reasons go out to every player, not just admins - a penalty nobody can see the reason
        // for is what starts the argument. Who applied it stays internal.
        entry.Adjustments
            .Select(a => new LeaderboardAdjustmentResponse(a.AdjustmentId, a.Points, a.Reason)).ToList(),
        entry.Voids
            .Select(v => new LeaderboardVoidResponse(v.PollId, v.PollTitle, v.Reason)).ToList(),
        entry.GameDaysPlayed, entry.IsRankShared, entry.TieBreak?.ToResponse());

    public static LeaderboardTieBreakResponse ToResponse(this TieBreak tieBreak) => new(
        tieBreak.LevelOnPointsWith
            .Select(r => new LeaderboardRivalResponse(r.UserId, r.DisplayName, r.GameDaysPlayed)).ToList(),
        tieBreak.SplitOnGameDaysPlayed,
        tieBreak.HeadToHead is { } record
            ? new LeaderboardHeadToHeadResponse(record.Won, record.Lost, record.Drawn)
            : null,
        tieBreak.SplitOnHeadToHead);

    public static LeaderboardResponse ToResponse(this LeaderboardResult result) => new(
        result.SeasonId, result.Entries.Select(e => e.ToResponse()).ToList());
}
