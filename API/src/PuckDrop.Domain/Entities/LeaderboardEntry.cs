namespace PuckDrop.Domain.Entities;

/// <summary>
/// A player's standing within a season, folded from their scoring facts.
/// </summary>
/// <remarks>
/// Not a stored running total: this is built on read by
/// <see cref="Standings.SeasonStandings.Build"/> from the player's <see cref="PollScore"/>,
/// <see cref="PollVoid"/> and <see cref="PointAdjustment"/> records. That is what lets a poll be
/// re-scored, a game day voided or points deducted - and every one of those undone - without any
/// delta ever having to be reversed.
/// </remarks>
public class LeaderboardEntry
{
    /// <summary>
    /// Cognito user ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Which season this entry belongs to.
    /// </summary>
    public required string SeasonId { get; set; }

    /// <summary>
    /// Denormalised display name for fast reads.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Points earned from scored polls, excluding any that were voided.
    /// </summary>
    public int EarnedPoints { get; set; }

    /// <summary>
    /// Net points from admin adjustments. Negative when deductions outweigh awards.
    /// </summary>
    public int AdjustmentPoints { get; set; }

    /// <summary>
    /// Effective season total. Can be negative when deductions exceed points earned - displayed
    /// and ranked as-is rather than clamped, so the arithmetic on the leaderboard adds up.
    /// </summary>
    public int TotalPoints => EarnedPoints + AdjustmentPoints;

    /// <summary>
    /// Graded questions answered across all polls, excluding any that were voided.
    /// </summary>
    public int TotalAnswered { get; set; }

    /// <summary>
    /// Last time any of the underlying facts changed (UTC).
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Adjustments making up <see cref="AdjustmentPoints"/>, oldest first, for public display.
    /// </summary>
    public IReadOnlyList<PointAdjustment> Adjustments { get; set; } = [];

    /// <summary>
    /// Polls voided for this player, oldest first, for public display.
    /// </summary>
    public IReadOnlyList<PollVoid> Voids { get; set; } = [];

    /// <summary>
    /// Accuracy percentage (0-100). Returns 0 if no questions answered.
    /// </summary>
    /// <remarks>
    /// Deliberately measured on <see cref="EarnedPoints"/>, not <see cref="TotalPoints"/>: an
    /// adjustment is a sanction, not a wrong answer, and must not rewrite someone's hit rate.
    /// </remarks>
    public double Accuracy => TotalAnswered > 0
        ? Math.Round((double)EarnedPoints / TotalAnswered * 100, 1)
        : 0;
}
