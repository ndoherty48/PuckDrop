namespace PuckDrop.Domain.Entities;

/// <summary>
/// An admin's decision to void one player's picks for one game day.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="PollScore"/>: re-scoring a poll rewrites the score fact,
/// and must not be able to wipe out a void. Keeping them apart also means a void can be recorded
/// before the poll is scored, because the fold reads both and the order never matters. Undoing a
/// void is a single delete - the total then restores exactly, with no stored delta to reverse.
/// </remarks>
public class PollVoid
{
    public required string SeasonId { get; set; }

    public required string PollId { get; set; }

    /// <summary>
    /// Cognito user ID of the player whose picks are voided.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Poll title captured at void time, so the leaderboard can name the voided game day without
    /// loading every poll.
    /// </summary>
    public required string PollTitle { get; set; }

    /// <summary>
    /// Why the picks were voided. Shown publicly on the leaderboard.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Cognito user ID of the admin who voided.
    /// </summary>
    public required string VoidedBy { get; set; }

    public DateTime VoidedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a void, validating the reason.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the reason is blank or too long.</exception>
    public static PollVoid Create(
        string seasonId, string pollId, string userId, string pollTitle, string reason, string voidedBy) => new()
        {
            SeasonId = seasonId,
            PollId = pollId,
            UserId = userId,
            PollTitle = pollTitle,
            Reason = SanctionReason.Normalise(reason, nameof(reason)),
            VoidedBy = voidedBy
        };
}
