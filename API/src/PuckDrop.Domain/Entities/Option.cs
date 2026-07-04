namespace PuckDrop.Domain.Entities;

/// <summary>
/// A possible answer to a question.
/// </summary>
public class Option
{
    /// <summary>
    /// Unique identifier (ULID).
    /// </summary>
    public required string OptionId { get; set; }

    /// <summary>
    /// Parent question identifier.
    /// </summary>
    public required string QuestionId { get; set; }

    /// <summary>
    /// The option text.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Display order within the question.
    /// </summary>
    public required int SortOrder { get; set; }
}
