using PuckDrop.Domain.Entities;

namespace PuckDrop.Application.Models;

/// <summary>
/// A poll with all its questions and options loaded.
/// </summary>
public record PollWithQuestions(
    GameDayPoll Poll,
    IReadOnlyList<Question> Questions,
    IReadOnlyList<Option> Options);

/// <summary>
/// A user's answer submission for a single question.
/// </summary>
public record AnswerSubmission(string QuestionId, string SelectedOptionId);

/// <summary>
/// The correct answer for a question, used during scoring.
/// </summary>
public record QuestionScore(string QuestionId, string CorrectOptionId);

/// <summary>
/// An option definition when creating a question.
/// </summary>
public record OptionDefinition(string Text, int SortOrder);

/// <summary>
/// Every scoring fact recorded for a season: what players earned, what an admin voided, and any
/// manual point adjustments. Folded into standings by SeasonStandings.Build - never accumulated.
/// </summary>
public record SeasonFacts(
    IReadOnlyList<PollScore> Scores,
    IReadOnlyList<PollVoid> Voids,
    IReadOnlyList<PointAdjustment> Adjustments);

/// <summary>
/// A fixture parsed from an external calendar feed, not yet created as a poll - a transient read
/// model for the bulk-import preview, not a persisted domain concept.
/// </summary>
public record FixtureCandidate(string Title, string Category, DateOnly GameDate, DateTime Deadline)
{
    /// <summary>
    /// Whether a poll with this title and game date already exists in that date's season - set by
    /// FixtureImportService, never by the feed fetcher itself, which only knows the calendar.
    /// </summary>
    public bool AlreadyImported { get; init; }
}
