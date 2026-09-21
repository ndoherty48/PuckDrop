namespace PuckDrop.Domain.Entities;

/// <summary>
/// A manual points adjustment applied to a player's season total by an admin.
/// </summary>
/// <remarks>
/// Signed: negative deducts, positive awards. Adjustments can take a total below zero - that is
/// deliberate, and the leaderboard displays and ranks negatives as-is rather than clamping, so the
/// arithmetic on screen always adds up. Removing an adjustment restores the total exactly.
/// </remarks>
public class PointAdjustment
{
    /// <summary>
    /// Sanity bound. A friend-group season is scored one point per correct answer, so anything
    /// beyond this is a typo rather than an intent.
    /// </summary>
    public const int MaxAbsolutePoints = 1000;

    public required string SeasonId { get; set; }

    public required string AdjustmentId { get; set; }

    /// <summary>
    /// Cognito user ID of the player being adjusted.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Denormalised display name. Needed because a player's name is only ever captured from their
    /// own answers, and there is no user directory to look it up in.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Signed points change. Negative deducts, positive awards.
    /// </summary>
    public required int Points { get; set; }

    /// <summary>
    /// Why the adjustment was made. Shown publicly on the leaderboard.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Cognito user ID of the admin who made the adjustment.
    /// </summary>
    public required string CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates an adjustment, validating the points change and the reason.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if points is zero or beyond the bound.</exception>
    /// <exception cref="ArgumentException">Thrown if the reason is blank or too long.</exception>
    public static PointAdjustment Create(
        string seasonId, string adjustmentId, string userId, string displayName, int points, string reason, string createdBy)
    {
        if (points == 0)
            throw new ArgumentOutOfRangeException(nameof(points), "An adjustment must change the total.");
        if (Math.Abs(points) > MaxAbsolutePoints)
            throw new ArgumentOutOfRangeException(
                nameof(points), $"An adjustment cannot be larger than {MaxAbsolutePoints} points.");

        return new PointAdjustment
        {
            SeasonId = seasonId,
            AdjustmentId = adjustmentId,
            UserId = userId,
            DisplayName = displayName,
            Points = points,
            Reason = SanctionReason.Normalise(reason, nameof(reason)),
            CreatedBy = createdBy
        };
    }
}
