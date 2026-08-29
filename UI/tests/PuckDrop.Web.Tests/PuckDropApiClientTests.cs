using System.Net;
using System.Net.Http.Json;
using PuckDrop.Web.Services;
using Xunit;

namespace PuckDrop.Web.Tests;

public class PuckDropApiClientTests
{
    /// <summary>
    /// Routes every request to a canned response, so tests can drive PuckDropApiClient's
    /// response-handling branches without a real server.
    /// </summary>
    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private static PuckDropApiClient CreateClient(HttpResponseMessage response)
    {
        var httpClient = new HttpClient(new StubHandler(response)) { BaseAddress = new Uri("http://localhost/") };
        return new PuckDropApiClient(httpClient);
    }

    // ─── GetAsync<T> (via GetCurrentSeasonAsync) ────────────────────────────

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNullRatherThanThrowing()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetCurrentSeasonAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_SuccessWithNonJsonContentType_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "text/plain")
        };

        var result = await CreateClient(response).GetCurrentSeasonAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_SuccessWithNoContentType_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentType = null;

        var result = await CreateClient(response).GetCurrentSeasonAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_SuccessWithJson_DeserializesResponse()
    {
        var season = new SeasonModel("2025-26", "2025/26 Season", new DateOnly(2025, 8, 1), new DateOnly(2026, 4, 30));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(season) };

        var result = await CreateClient(response).GetCurrentSeasonAsync();

        Assert.NotNull(result);
        Assert.Equal("2025-26", result.SeasonId);
    }

    [Fact]
    public async Task GetAsync_ServerError_ThrowsApiExceptionWithStatusAndBody()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        };

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateClient(response).GetCurrentSeasonAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("boom", ex.Message);
    }

    // ─── EnsureSuccessAsync (via CreatePollAsync - a separate code path from GetAsync<T>) ──

    [Fact]
    public async Task CreatePollAsync_ServerError_ThrowsApiException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid title")
        };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateClient(response).CreatePollAsync(BuildCreatePollRequest()));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("invalid title", ex.Message);
    }

    [Fact]
    public async Task CreatePollAsync_Success_ReturnsDeserializedPoll()
    {
        var poll = new PollModel(
            "poll-1", "2025-26", new DateOnly(2026, 1, 15), "Title",
            new DateTime(2026, 1, 15, 19, 0, 0, DateTimeKind.Utc), "Draft", "admin",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(poll) };

        var result = await CreateClient(response).CreatePollAsync(BuildCreatePollRequest());

        Assert.NotNull(result);
        Assert.Equal("poll-1", result.PollId);
    }

    private static CreatePollRequest BuildCreatePollRequest() =>
        new("Title", new DateOnly(2026, 1, 15), new DateTime(2026, 1, 15, 19, 0, 0, DateTimeKind.Utc));
}
