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
    string UserId, string DisplayName, List<UserAnswerModel> Answers, int Points,
    bool IsVoided = false, string? VoidReason = null);

// ─── Leaderboard ──────────────────────────────────────────────────────────────

public record LeaderboardModel(string SeasonId, List<LeaderboardEntryModel> Entries);

public record LeaderboardEntryModel(
    string UserId, string DisplayName, int TotalPoints, int TotalAnswered, int Rank,
    int EarnedPoints, int AdjustmentPoints,
    List<LeaderboardAdjustmentModel> Adjustments, List<LeaderboardVoidModel> Voids);

/// <summary>A point adjustment, shown publicly so a penalty is always explained.</summary>
public record LeaderboardAdjustmentModel(string AdjustmentId, int Points, string Reason);

/// <summary>A game day whose picks were voided for this player, with its reason.</summary>
public record LeaderboardVoidModel(string PollId, string PollTitle, string Reason);

// ─── Auth ─────────────────────────────────────────────────────────────────────

/// <summary>
/// OIDC config fetched at boot in Program.cs; matches the API's AuthConfigResponse.
/// </summary>
public record AuthConfigModel(string Authority, string ClientId, string ResponseType, bool UseCognitoLogout = false);

// ─── Requests ─────────────────────────────────────────────────────────────────

public record CreatePollRequest(string Title, DateOnly GameDate, DateTime Deadline);

public record CreateQuestionRequest(string Text, int SortOrder, List<CreateOptionRequest> Options);

public record CreateOptionRequest(string Text, int SortOrder);

public record SubmitAnswersRequest(List<AnswerItem> Answers);

public record AnswerItem(string QuestionId, string SelectedOptionId);

public record ScorePollRequest(List<ScoreItem> Answers);

public record ScoreItem(string QuestionId, string CorrectOptionId);

public record VoidPicksRequest(string UserId, string Reason);

public record CreateAdjustmentRequest(string UserId, int Points, string Reason, string? SeasonId = null);
