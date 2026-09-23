using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PuckDrop.Api.Auth;
using Xunit;

namespace PuckDrop.Api.Tests;

/// <summary>
/// Covers the userInfo fallback for Cognito tokens, which have no name claims. A stub discovery
/// document and a fake handler stand in for Cognito.
/// </summary>
public class UserProfileServiceTests
{
    private const string UserInfoEndpoint = "https://auth.example.com/oauth2/userInfo";
    private const string Sub = "91eb4550-9091-708c-a7a6-9758ef8b6b1e";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static UserProfileService CreateService(HttpMessageHandler handler, params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        httpContext.Request.Headers.Authorization = "Bearer access-token-123";

        var jwtBearerOptions = new JwtBearerOptions
        {
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { UserInfoEndpoint = UserInfoEndpoint })
        };

        return new UserProfileService(
            new HttpContextAccessor { HttpContext = httpContext },
            new FakeHttpClientFactory(handler),
            new FixedOptionsMonitor<JwtBearerOptions>(jwtBearerOptions),
            NullLogger<UserProfileService>.Instance);
    }

    [Fact]
    public async Task GetDisplayNameAsync_NameClaimPresent_ReturnsItWithoutCallingUserInfo()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("userInfo should not be called."));
        var service = CreateService(handler, new Claim("sub", Sub), new Claim("preferred_username", "nathan"));

        var displayName = await service.GetDisplayNameAsync(TestContext.Current.CancellationToken);

        Assert.Equal("nathan", displayName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetDisplayNameAsync_NoNameClaims_CallsUserInfoWithBearerToken_ReturnsPreferredUsername()
    {
        var handler = new FakeHandler(_ => Json($$"""{"sub":"{{Sub}}","preferred_username":"nathan","email":"nathan@example.com"}"""));
        var service = CreateService(handler, new Claim("sub", Sub));

        var displayName = await service.GetDisplayNameAsync(TestContext.Current.CancellationToken);

        Assert.Equal("nathan", displayName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(UserInfoEndpoint, request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("access-token-123", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetDisplayNameAsync_UserInfoHasName_PrefersItOverPreferredUsername()
    {
        var handler = new FakeHandler(_ => Json("""{"name":"Nathan Doherty","preferred_username":"nathan"}"""));
        var service = CreateService(handler, new Claim("sub", Sub));

        Assert.Equal("Nathan Doherty", await service.GetDisplayNameAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDisplayNameAsync_UserInfoHasOnlyEmail_ReturnsEmail()
    {
        var handler = new FakeHandler(_ => Json("""{"email":"nathan@example.com"}"""));
        var service = CreateService(handler, new Claim("sub", Sub));

        Assert.Equal("nathan@example.com", await service.GetDisplayNameAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDisplayNameAsync_UserInfoReturns401_FallsBackToSub()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = CreateService(handler, new Claim("sub", Sub));

        Assert.Equal(Sub, await service.GetDisplayNameAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDisplayNameAsync_UserInfoThrows_FallsBackToSub()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        var service = CreateService(handler, new Claim("sub", Sub));

        Assert.Equal(Sub, await service.GetDisplayNameAsync(TestContext.Current.CancellationToken));
    }
}
