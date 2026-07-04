using System.Net.Http.Json;

namespace PuckDrop.Web.Services;

/// <summary>
/// Typed HTTP client for the PuckDrop API.
/// </summary>
public class PuckDropApiClient(HttpClient httpClient)
{
    // ─── Seasons ──────────────────────────────────────────────────────────────

    public Task<List<SeasonModel>?> GetSeasonsAsync() =>
        httpClient.GetFromJsonAsync<List<SeasonModel>>("seasons");

    public Task<SeasonModel?> GetCurrentSeasonAsync() =>
        httpClient.GetFromJsonAsync<SeasonModel>("seasons/current");

    // ─── Polls ────────────────────────────────────────────────────────────────

    public Task<List<PollModel>?> GetPollsAsync(string seasonId) =>
        httpClient.GetFromJsonAsync<List<PollModel>>($"polls?seasonId={seasonId}");

    public Task<List<PollModel>?> GetActivePollsAsync(string seasonId) =>
        httpClient.GetFromJsonAsync<List<PollModel>>($"polls/active?seasonId={seasonId}");

    public Task<PollDetailModel?> GetPollAsync(string pollId) =>
        httpClient.GetFromJsonAsync<PollDetailModel>($"polls/{pollId}");

    public async Task<PollModel?> CreatePollAsync(CreatePollRequest request)
    {
        var response = await httpClient.PostAsJsonAsync("polls", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PollModel>();
    }

    public async Task<PollModel?> PublishPollAsync(string pollId)
    {
        var response = await httpClient.PostAsync($"polls/{pollId}/publish", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PollModel>();
    }

    public async Task<PollModel?> ClosePollAsync(string pollId)
    {
        var response = await httpClient.PostAsync($"polls/{pollId}/close", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PollModel>();
    }

    // ─── Questions ────────────────────────────────────────────────────────────

    public async Task<QuestionModel?> AddQuestionAsync(string pollId, CreateQuestionRequest request)
    {
        var response = await httpClient.PostAsJsonAsync($"polls/{pollId}/questions", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<QuestionModel>();
    }

    public async Task DeleteQuestionAsync(string pollId, string questionId)
    {
        var response = await httpClient.DeleteAsync($"polls/{pollId}/questions/{questionId}");
        response.EnsureSuccessStatusCode();
    }

    // ─── Answers ──────────────────────────────────────────────────────────────

    public Task<List<UserAnswerModel>?> GetAnswersAsync(string pollId) =>
        httpClient.GetFromJsonAsync<List<UserAnswerModel>>($"polls/{pollId}/answers");

    public async Task<List<UserAnswerModel>?> SubmitAnswersAsync(string pollId, SubmitAnswersRequest request)
    {
        var response = await httpClient.PutAsJsonAsync($"polls/{pollId}/answers", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UserAnswerModel>>();
    }

    // ─── Scoring ──────────────────────────────────────────────────────────────

    public async Task ScorePollAsync(string pollId, ScorePollRequest request)
    {
        var response = await httpClient.PostAsJsonAsync($"polls/{pollId}/score", request);
        response.EnsureSuccessStatusCode();
    }

    // ─── Results ──────────────────────────────────────────────────────────────

    public Task<PollResultsModel?> GetResultsAsync(string pollId) =>
        httpClient.GetFromJsonAsync<PollResultsModel>($"polls/{pollId}/results");

    // ─── Leaderboard ──────────────────────────────────────────────────────────

    public Task<LeaderboardModel?> GetLeaderboardAsync(string? seasonId = null) =>
        httpClient.GetFromJsonAsync<LeaderboardModel>(
            seasonId is not null ? $"leaderboard?seasonId={seasonId}" : "leaderboard");
}
