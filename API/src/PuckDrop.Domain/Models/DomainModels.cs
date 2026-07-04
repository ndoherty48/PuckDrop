using PuckDrop.Domain.Entities;

namespace PuckDrop.Domain.Models;

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
