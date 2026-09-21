namespace PuckDrop.Domain.Entities;

/// <summary>
/// What one player earned in one scored poll.
/// </summary>
/// <remarks>
/// Written when a poll is scored and rewritten on every re-score, so it is always the current truth
/// for that (player, poll) pair. Season totals are folded from these rather than accumulated into a
/// running sum, which is what makes re-scoring, voiding and point adjustments safe - and undoable.
/// </remarks>
public class PollScore
{
    public required string SeasonId { get; set; }

    public required string PollId { get; set; }

    /// <summary>
    /// Cognito user ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Display name captured at scoring time from the player's own answers. Refreshed on every
    /// scoring pass, so a name captured incorrectly self-heals rather than staying stale forever.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Points earned in this poll - one per correct answer.
    /// </summary>
    public required int Points { get; set; }

    /// <summary>
    /// How many of the player's answers in this poll were actually graded. Ungraded answers (a
    /// question the admin never set a correct option for) are excluded, so a partially scored poll
    /// doesn't quietly tank everyone's accuracy.
    /// </summary>
    public required int Answered { get; set; }

    public DateTime ScoredAt { get; set; } = DateTime.UtcNow;
}
