using System.Net;
using System.Net.Http.Json;

namespace PuckDrop.Web.Services;

/// <summary>
/// Typed HTTP client for the PuckDrop API.
/// Handles non-success status codes and non-JSON responses gracefully.
/// </summary>
public class PuckDropApiClient(HttpClient httpClient)
{
    // ─── Seasons ──────────────────────────────────────────────────────────────

    public Task<List<SeasonModel>?> GetSeasonsAsync() =>
        GetAsync<List<SeasonModel>>("seasons");

    public Task<SeasonModel?> GetCurrentSeasonAsync() =>
        GetAsync<SeasonModel>("seasons/current");

    // ─── Polls ────────────────────────────────────────────────────────────────

    public Task<List<PollModel>?> GetPollsAsync(string seasonId) =>
        GetAsync<List<PollModel>>($"polls?seasonId={seasonId}");

    public Task<List<PollModel>?> GetActivePollsAsync(string seasonId) =>
        GetAsync<List<PollModel>>($"polls/active?seasonId={seasonId}");

    public Task<PollDetailModel?> GetPollAsync(string pollId) =>
        GetAsync<PollDetailModel>($"polls/{pollId}");

    public async Task<PollModel?> CreatePollAsync(CreatePollRequest request)
    {
        var response = await httpClient.PostAsJsonAsync("polls", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PollModel>();
    }

    public async Task<PollModel?> PublishPollAsync(string pollId)
    {
        var response = await httpClient.PostAsync($"polls/{pollId}/publish", null);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PollModel>();
    }

    public async Task<PollModel?> ClosePollAsync(string pollId)
    {
        var response = await httpClient.PostAsync($"polls/{pollId}/close", null);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PollModel>();
    }

    // ─── Questions ────────────────────────────────────────────────────────────

    public async Task<QuestionModel?> AddQuestionAsync(string pollId, CreateQuestionRequest request)
    {
        var response = await httpClient.PostAsJsonAsync($"polls/{pollId}/questions", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<QuestionModel>();
    }

    public async Task DeleteQuestionAsync(string pollId, string questionId)
    {
        var response = await httpClient.DeleteAsync($"polls/{pollId}/questions/{questionId}");
        await EnsureSuccessAsync(response);
    }

    // ─── Answers ──────────────────────────────────────────────────────────────

    public Task<List<UserAnswerModel>?> GetAnswersAsync(string pollId) =>
        GetAsync<List<UserAnswerModel>>($"polls/{pollId}/answers");

    public async Task<List<UserAnswerModel>?> SubmitAnswersAsync(string pollId, SubmitAnswersRequest request)
    {
        var response = await httpClient.PutAsJsonAsync($"polls/{pollId}/answers", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<UserAnswerModel>>();
    }

    // ─── Scoring ──────────────────────────────────────────────────────────────

    public async Task ScorePollAsync(string pollId, ScorePollRequest request)
    {
        var response = await httpClient.PostAsJsonAsync($"polls/{pollId}/score", request);
        await EnsureSuccessAsync(response);
    }

    // ─── Results ──────────────────────────────────────────────────────────────

    public Task<PollResultsModel?> GetResultsAsync(string pollId) =>
        GetAsync<PollResultsModel>($"polls/{pollId}/results");

    // ─── Leaderboard ──────────────────────────────────────────────────────────

    public Task<LeaderboardModel?> GetLeaderboardAsync(string? seasonId = null) =>
        GetAsync<LeaderboardModel>(
            seasonId is not null ? $"leaderboard?seasonId={seasonId}" : "leaderboard");

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<T?> GetAsync<T>(string url)
    {
        var response = await httpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new ApiException(response.StatusCode, body);
        }

        // Guard against empty or non-JSON responses
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.Contains("json"))
            return default;

        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new ApiException(response.StatusCode, body);
        }
    }
}

public class ApiException(HttpStatusCode statusCode, string body)
    : Exception($"API returned {(int)statusCode}: {body}")
{
    public HttpStatusCode StatusCode => statusCode;
}
