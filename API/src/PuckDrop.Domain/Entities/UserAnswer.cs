namespace PuckDrop.Domain.Entities;

/// <summary>
/// A user's selected answer for a question.
/// </summary>
public class UserAnswer
{
    /// <summary>
    /// Cognito user ID.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Display name captured at submission, while the answerer is the current user (at scoring
    /// it's the admin). Seeds <see cref="LeaderboardEntry.DisplayName"/>.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Which question this answer is for.
    /// </summary>
    public required string QuestionId { get; set; }

    /// <summary>
    /// Which poll this answer belongs to (denormalised for queries).
    /// </summary>
    public required string PollId { get; set; }

    /// <summary>
    /// The user's chosen option.
    /// </summary>
    public required string SelectedOptionId { get; set; }

    /// <summary>
    /// Last submission/edit time (UTC).
    /// </summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Null until scored, then true/false.
    /// </summary>
    public bool? IsCorrect { get; private set; }

    /// <summary>
    /// Evaluates this answer against the correct option.
    /// </summary>
    /// <param name="correctOptionId">The correct option ID for the question.</param>
    public void Evaluate(string correctOptionId)
    {
        IsCorrect = SelectedOptionId == correctOptionId;
    }
}
