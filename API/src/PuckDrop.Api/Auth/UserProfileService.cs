using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using PuckDrop.Application.Services.Abstractions;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Resolves the current user's display name. Keycloak access tokens include one; Cognito's don't,
/// so it falls back to the provider's userInfo endpoint with the caller's own access token.
/// </summary>
public class UserProfileService(
    IHttpContextAccessor httpContextAccessor,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<JwtBearerOptions> jwtBearerOptions,
    ILogger<UserProfileService> logger) : IUserProfileService
{
    // Same precedence as ClaimsPrincipalExtensions.TryGetDisplayNameFromClaims and the UI's
    // PuckDropClaimsPrincipalFactory, so every surface picks the same value.
    private static readonly string[] UserInfoNameProperties = ["name", "preferred_username", "email"];

    public async Task<string> GetDisplayNameAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No current HTTP request to resolve a display name for.");
        var user = httpContext.User;

        return user.TryGetDisplayNameFromClaims()
            ?? await TryGetDisplayNameFromUserInfoAsync(httpContext, cancellationToken)
            ?? user.GetUserId();
    }

    private async Task<string?> TryGetDisplayNameFromUserInfoAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!AuthenticationHeaderValue.TryParse(httpContext.Request.Headers.Authorization.ToString(), out var authorization)
            || !string.Equals(authorization.Scheme, JwtBearerDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(authorization.Parameter))
        {
            return null;
        }

        try
        {
            // Reuse the JWT handler's cached discovery document for the userInfo URL.
            var configurationManager = jwtBearerOptions.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager;
            if (configurationManager is null)
                return null;

            var configuration = await configurationManager.GetConfigurationAsync(cancellationToken);
            if (string.IsNullOrEmpty(configuration.UserInfoEndpoint))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, configuration.UserInfoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, authorization.Parameter);

            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("userInfo lookup for a display name returned {StatusCode}; falling back to the user ID.", (int)response.StatusCode);
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            foreach (var property in UserInfoNameProperties)
            {
                if (document.RootElement.TryGetProperty(property, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }

            return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "userInfo lookup for a display name failed; falling back to the user ID.");
            return null;
        }
    }
}
