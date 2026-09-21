using PuckDrop.Web.Services;

namespace PuckDrop.Web.Tests.TestSupport;

/// <summary>
/// Builds leaderboard entries without every call site having to spell out the sanction fields.
/// </summary>
/// <remarks>
/// Earned points default to the effective total, which is what a player with no adjustments looks
/// like. Pass <paramref name="earnedPoints"/> to describe someone carrying a deduction: the
/// adjustment total is then whatever is left over, so the arithmetic in the fixture always adds up.
/// </remarks>
public static class LeaderboardEntries
{
    public static LeaderboardEntryModel Entry(
        string userId,
        string displayName,
        int totalPoints,
        int totalAnswered,
        int rank,
        int? earnedPoints = null,
        List<LeaderboardAdjustmentModel>? adjustments = null,
        List<LeaderboardVoidModel>? voids = null)
    {
        var earned = earnedPoints ?? totalPoints;

        return new LeaderboardEntryModel(
            userId, displayName, totalPoints, totalAnswered, rank,
            earned, totalPoints - earned, adjustments ?? [], voids ?? []);
    }
}
