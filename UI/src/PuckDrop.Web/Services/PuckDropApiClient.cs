using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    // ─── Voids ────────────────────────────────────────────────────────────────

    public async Task VoidPicksAsync(string pollId, VoidPicksRequest request)
    {
        var response = await httpClient.PostAsJsonAsync($"polls/{pollId}/voids", request);
        await EnsureSuccessAsync(response);
    }

    public async Task RestorePicksAsync(string pollId, string userId)
    {
        var response = await httpClient.DeleteAsync($"polls/{pollId}/voids/{userId}");
        await EnsureSuccessAsync(response);
    }

    // ─── Results ──────────────────────────────────────────────────────────────

    public Task<PollResultsModel?> GetResultsAsync(string pollId) =>
        GetAsync<PollResultsModel>($"polls/{pollId}/results");

    // ─── Leaderboard ──────────────────────────────────────────────────────────

    public Task<LeaderboardModel?> GetLeaderboardAsync(string? seasonId = null) =>
        GetAsync<LeaderboardModel>(
            seasonId is not null ? $"leaderboard?seasonId={seasonId}" : "leaderboard");

    // ─── Adjustments ──────────────────────────────────────────────────────────

    public async Task AddAdjustmentAsync(CreateAdjustmentRequest request)
    {
        var response = await httpClient.PostAsJsonAsync("leaderboard/adjustments", request);
        await EnsureSuccessAsync(response);
    }

    /// <summary>
    /// Removes an adjustment. The user is part of the key, not just the id.
    /// </summary>
    public async Task RemoveAdjustmentAsync(string adjustmentId, string userId, string? seasonId = null)
    {
        var url = $"leaderboard/adjustments/{adjustmentId}?userId={Uri.EscapeDataString(userId)}";
        if (seasonId is not null)
            url += $"&seasonId={Uri.EscapeDataString(seasonId)}";

        var response = await httpClient.DeleteAsync(url);
        await EnsureSuccessAsync(response);
    }

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

    /// <summary>The raw error response, for callers that need to tell one 400 from another.</summary>
    public string Body => body;

    /// <summary>
    /// The API's own error message, when the body is the standard error shape.
    /// </summary>
    /// <remarks>
    /// Null when the body isn't ours - notably a 404 from a route that doesn't exist at all, which
    /// is worth telling apart from a 404 the API deliberately returned.
    /// </remarks>
    public string? ApiMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                var error = JsonSerializer.Deserialize<ErrorResponse>(
                    body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

/// <summary>Matches the API's ErrorResponse contract.</summary>
public record ErrorResponse(string? Error, string? Message);
