using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PuckDrop.E2ETests.Browser;

/// <summary>
/// A minimal local HTTP server serving one canned ICS response, for the Import fixtures E2E test.
/// The API's IcsFixtureFeedFetcher runs as a plain local process here (like everything in this
/// AppHost except DynamoDB and Keycloak, which are containers) - 127.0.0.1 is reachable from it
/// the same as from this test process. Keeping the feed local (rather than a real third-party
/// site) keeps the E2E suite hermetic: it never depends on an external site's uptime or content.
/// </summary>
internal sealed class IcsTestServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly string _content;
    private readonly Task _acceptLoop;

    private IcsTestServer(HttpListener listener, string url, string content)
    {
        _listener = listener;
        Url = url;
        _content = content;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string Url { get; }

    public static Task<IcsTestServer> StartAsync(string icsContent)
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        return Task.FromResult(new IcsTestServer(listener, $"{prefix}fixtures.ics", icsContent));
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return; // Listener was stopped.
            }

            var bytes = Encoding.UTF8.GetBytes(_content);
            context.Response.ContentType = "text/calendar";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.OutputStream.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        try
        {
            await _acceptLoop;
        }
        catch
        {
            // Expected once the listener is stopped mid-accept.
        }
    }
}
