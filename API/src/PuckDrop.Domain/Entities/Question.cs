namespace PuckDrop.Domain.Entities;

/// <summary>
/// A multiple-choice question within a poll.
/// </summary>
public class Question
{
    /// <summary>
    /// Unique identifier (ULID).
    /// </summary>
    public required string QuestionId { get; set; }

    /// <summary>
    /// Parent poll identifier.
    /// </summary>
    public required string PollId { get; set; }

    /// <summary>
    /// The question text.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Display order within the poll.
    /// </summary>
    public required int SortOrder { get; set; }

    /// <summary>
    /// The correct option, set by admin post-game. Null until scored.
    /// </summary>
    public string? CorrectOptionId { get; private set; }

    /// <summary>
    /// Returns true if this question has been scored.
    /// </summary>
    public bool IsScored => CorrectOptionId is not null;

    /// <summary>
    /// Marks the correct answer for this question.
    /// </summary>
    /// <param name="optionId">The ID of the correct option.</param>
    /// <exception cref="ArgumentException">Thrown if optionId is null or empty.</exception>
    public void SetCorrectOption(string optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId))
            throw new ArgumentException("Option ID cannot be null or empty.", nameof(optionId));

        CorrectOptionId = optionId;
    }
}
