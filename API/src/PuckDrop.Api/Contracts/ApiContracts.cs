namespace PuckDrop.Api.Contracts;

// ─── Error ────────────────────────────────────────────────────────────────────

public record ErrorResponse(string Error, string Message);

// ─── Auth ─────────────────────────────────────────────────────────────────────

/// <summary>
/// OIDC config the Blazor app fetches at boot - see PuckDrop.Api.Auth.AuthDiscoveryOptions.
/// </summary>
public record AuthConfigResponse(string Authority, string ClientId, string ResponseType, bool UseCognitoLogout = false);

// ─── Seasons ──────────────────────────────────────────────────────────────────

public record SeasonResponse(string SeasonId, string Name, string StartDate, string EndDate);

// ─── Polls ────────────────────────────────────────────────────────────────────

public record CreatePollRequest(string Title, string GameDate, string Deadline);

public record UpdatePollRequest(string? Title, string? Deadline);

/// <summary>Draft-only: see PollService.RescheduleAsync.</summary>
public record RescheduleRequest(string GameDate, string Deadline);

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

// ─── Fixture import ───────────────────────────────────────────────────────────

public record FixtureImportPreviewRequest(string IcsUrl, string StartDate, string? EndDate);

public record FixtureImportPreviewResponse(IReadOnlyList<FixtureImportItem> Fixtures);

public record FixtureImportItem(string Title, string Category, string GameDate, string Deadline, bool AlreadyImported);

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

/// <summary>
/// Voids one player's picks for one game day. The reason is shown publicly on the leaderboard.
/// </summary>
public record VoidPicksRequest(string UserId, string Reason);

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
    int Points,
    bool IsVoided,
    string? VoidReason);

// ─── Leaderboard ──────────────────────────────────────────────────────────────

public record LeaderboardResponse(string SeasonId, IReadOnlyList<LeaderboardEntryResponse> Entries);

public record LeaderboardEntryResponse(
    string UserId,
    string DisplayName,
    int TotalPoints,
    int TotalAnswered,
    int Rank,
    int EarnedPoints,
    int AdjustmentPoints,
    IReadOnlyList<LeaderboardAdjustmentResponse> Adjustments,
    IReadOnlyList<LeaderboardVoidResponse> Voids);

/// <summary>
/// Applies a signed points adjustment. Negative deducts, positive awards; the reason is public.
/// </summary>
public record CreateAdjustmentRequest(string UserId, int Points, string Reason, string? SeasonId = null);

/// <summary>
/// A point adjustment shown publicly beside a player's total, so a penalty is always explained.
/// </summary>
public record LeaderboardAdjustmentResponse(string AdjustmentId, int Points, string Reason);

/// <summary>
/// A game day whose picks were voided for this player, shown publicly with its reason.
/// </summary>
public record LeaderboardVoidResponse(string PollId, string PollTitle, string Reason);
