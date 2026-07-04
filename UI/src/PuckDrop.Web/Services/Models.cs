namespace PuckDrop.Web.Services;

// ─── Seasons ──────────────────────────────────────────────────────────────────

public record SeasonModel(string SeasonId, string Name, string StartDate, string EndDate);

// ─── Polls ────────────────────────────────────────────────────────────────────

public record PollModel(
    string PollId, string SeasonId, string GameDate, string Title,
    string Deadline, string Status, string CreatedBy, string CreatedAt);

public record PollDetailModel(
    string PollId, string SeasonId, string GameDate, string Title,
    string Deadline, string Status, string CreatedBy, string CreatedAt,
    List<QuestionModel> Questions);

public record QuestionModel(
    string QuestionId, string Text, int SortOrder,
    string? CorrectOptionId, List<OptionModel> Options);

public record OptionModel(string OptionId, string Text, int SortOrder);

// ─── Answers ──────────────────────────────────────────────────────────────────

public record UserAnswerModel(string QuestionId, string SelectedOptionId, string SubmittedAt, bool? IsCorrect);

// ─── Results ──────────────────────────────────────────────────────────────────

public record PollResultsModel(
    string PollId, string Status, List<QuestionModel> Questions, List<UserResultModel> UserResults);

public record UserResultModel(
    string UserId, string DisplayName, List<UserAnswerModel> Answers, int Points);

// ─── Leaderboard ──────────────────────────────────────────────────────────────

public record LeaderboardModel(string SeasonId, List<LeaderboardEntryModel> Entries);

public record LeaderboardEntryModel(
    string UserId, string DisplayName, int TotalPoints, int TotalAnswered, int Rank);

// ─── Requests ─────────────────────────────────────────────────────────────────

public record CreatePollRequest(string Title, string GameDate, string Deadline);

public record CreateQuestionRequest(string Text, int SortOrder, List<CreateOptionRequest> Options);

public record CreateOptionRequest(string Text, int SortOrder);

public record SubmitAnswersRequest(List<AnswerItem> Answers);

public record AnswerItem(string QuestionId, string SelectedOptionId);

public record ScorePollRequest(List<ScoreItem> Answers);

public record ScoreItem(string QuestionId, string CorrectOptionId);
