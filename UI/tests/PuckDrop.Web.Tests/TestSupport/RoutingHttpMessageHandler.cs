using System.Net;
using System.Net.Http.Json;
using PuckDrop.Web.Services;

namespace PuckDrop.Web.Tests.TestSupport;

/// <summary>
/// Routes fake HTTP responses by method + path/query, for building a <see cref="PuckDropApiClient"/>
/// backed by canned data instead of a real server. A single rendered component often calls
/// several endpoints in one lifecycle (e.g. Poll.razor calls both GetPollAsync and
/// GetAnswersAsync), which the simpler single-fixed-response fake in PuckDropApiClientTests
/// doesn't need to support but this one does.
/// </summary>
public class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string PathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public RoutingHttpMessageHandler Map(HttpMethod method, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((method, pathAndQuery, respond));
        return this;
    }

    public RoutingHttpMessageHandler MapJson<T>(HttpMethod method, string pathAndQuery, T body) =>
        Map(method, pathAndQuery, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });

    public RoutingHttpMessageHandler MapEmptySuccess(HttpMethod method, string pathAndQuery) =>
        Map(method, pathAndQuery, _ => new HttpResponseMessage(HttpStatusCode.OK));

    /// <summary>Wraps this handler in a PuckDropApiClient pointed at a dummy local base address.</summary>
    public PuckDropApiClient BuildClient() =>
        new(new HttpClient(this) { BaseAddress = new Uri("http://localhost/") });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');
        var route = _routes.FirstOrDefault(r => r.Method == request.Method && r.PathAndQuery == pathAndQuery);

        if (route.Respond is null)
            throw new InvalidOperationException($"No fake route registered for {request.Method} {pathAndQuery}.");

        return Task.FromResult(route.Respond(request));
    }
}
