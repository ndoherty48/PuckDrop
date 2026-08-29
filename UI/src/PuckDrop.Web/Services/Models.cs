namespace PuckDrop.Web.Services;

// ─── Seasons ──────────────────────────────────────────────────────────────────

public record SeasonModel(string SeasonId, string Name, DateOnly StartDate, DateOnly EndDate);

// ─── Polls ────────────────────────────────────────────────────────────────────

public record PollModel(
    string PollId, string SeasonId, DateOnly GameDate, string Title,
    DateTime Deadline, string Status, string CreatedBy, DateTime CreatedAt);

public record PollDetailModel(
    string PollId, string SeasonId, DateOnly GameDate, string Title,
    DateTime Deadline, string Status, string CreatedBy, DateTime CreatedAt,
    List<QuestionModel> Questions);

public record QuestionModel(
    string QuestionId, string Text, int SortOrder,
    string? CorrectOptionId, List<OptionModel> Options);

public record OptionModel(string OptionId, string Text, int SortOrder);

// ─── Answers ──────────────────────────────────────────────────────────────────

public record UserAnswerModel(string QuestionId, string SelectedOptionId, DateTime SubmittedAt, bool? IsCorrect);

// ─── Results ──────────────────────────────────────────────────────────────────

public record PollResultsModel(
    string PollId, string Status, List<QuestionModel> Questions, List<UserResultModel> UserResults);

public record UserResultModel(
    string UserId, string DisplayName, List<UserAnswerModel> Answers, int Points);

// ─── Leaderboard ──────────────────────────────────────────────────────────────

public record LeaderboardModel(string SeasonId, List<LeaderboardEntryModel> Entries);

public record LeaderboardEntryModel(
    string UserId, string DisplayName, int TotalPoints, int TotalAnswered, int Rank);

// ─── Auth ─────────────────────────────────────────────────────────────────────

/// <summary>
/// OIDC config fetched from the unauthenticated GET auth-config endpoint at boot, in Program.cs -
/// matches PuckDrop.Api.Contracts.AuthConfigResponse.
/// </summary>
public record AuthConfigModel(string Authority, string ClientId, string ResponseType);

// ─── Requests ─────────────────────────────────────────────────────────────────

public record CreatePollRequest(string Title, DateOnly GameDate, DateTime Deadline);

public record CreateQuestionRequest(string Text, int SortOrder, List<CreateOptionRequest> Options);

public record CreateOptionRequest(string Text, int SortOrder);

public record SubmitAnswersRequest(List<AnswerItem> Answers);

public record AnswerItem(string QuestionId, string SelectedOptionId);

public record ScorePollRequest(List<ScoreItem> Answers);

public record ScoreItem(string QuestionId, string CorrectOptionId);
