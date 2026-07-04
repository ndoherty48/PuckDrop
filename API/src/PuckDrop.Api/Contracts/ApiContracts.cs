namespace PuckDrop.Api.Contracts;

// ─── Error ────────────────────────────────────────────────────────────────────

public record ErrorResponse(string Error, string Message);

// ─── Seasons ──────────────────────────────────────────────────────────────────

public record SeasonResponse(string SeasonId, string Name, string StartDate, string EndDate);

// ─── Polls ────────────────────────────────────────────────────────────────────

public record CreatePollRequest(string Title, string GameDate, string Deadline);

public record UpdatePollRequest(string? Title, string? Deadline);

public record PollResponse(
    string PollId,
    string SeasonId,
    string GameDate,
    string Title,
    string Deadline,
    string Status,
    string CreatedBy,
    string CreatedAt);

public record PollDetailResponse(
    string PollId,
    string SeasonId,
    string GameDate,
    string Title,
    string Deadline,
    string Status,
    string CreatedBy,
    string CreatedAt,
    IReadOnlyList<QuestionResponse> Questions);

// ─── Questions ────────────────────────────────────────────────────────────────

public record CreateQuestionRequest(string Text, int SortOrder, IReadOnlyList<CreateOptionRequest> Options);

public record CreateOptionRequest(string Text, int SortOrder);

public record UpdateQuestionRequest(string Text, int SortOrder, IReadOnlyList<CreateOptionRequest> Options);

public record QuestionResponse(
    string QuestionId,
    string Text,
    int SortOrder,
    string? CorrectOptionId,
    IReadOnlyList<OptionResponse> Options);

public record OptionResponse(string OptionId, string Text, int SortOrder);

// ─── Answers ──────────────────────────────────────────────────────────────────

public record SubmitAnswersRequest(IReadOnlyList<AnswerItem> Answers);

public record AnswerItem(string QuestionId, string SelectedOptionId);

public record UserAnswerResponse(string QuestionId, string SelectedOptionId, string SubmittedAt, bool? IsCorrect);

// ─── Scoring ──────────────────────────────────────────────────────────────────

public record ScorePollRequest(IReadOnlyList<ScoreItem> Answers);

public record ScoreItem(string QuestionId, string CorrectOptionId);

// ─── Results ──────────────────────────────────────────────────────────────────

public record PollResultsResponse(
    string PollId,
    string Status,
    IReadOnlyList<QuestionResponse> Questions,
    IReadOnlyList<UserResultResponse> UserResults);

public record UserResultResponse(
    string UserId,
    string DisplayName,
    IReadOnlyList<UserAnswerResponse> Answers,
    int Points);

// ─── Leaderboard ──────────────────────────────────────────────────────────────

public record LeaderboardResponse(string SeasonId, IReadOnlyList<LeaderboardEntryResponse> Entries);

public record LeaderboardEntryResponse(
    string UserId,
    string DisplayName,
    int TotalPoints,
    int TotalAnswered,
    int Rank);
