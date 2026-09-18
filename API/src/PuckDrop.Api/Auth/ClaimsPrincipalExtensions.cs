using System.Security.Claims;

namespace PuckDrop.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the authenticated user's ID from the Cognito 'sub' claim.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("User ID claim not found.");
    }

    /// <summary>
    /// Gets the authenticated user's display name from Cognito/Keycloak claims.
    /// Falls back to username, then email, then user ID.
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal principal)
    {
        return principal.TryGetDisplayNameFromClaims() ?? principal.GetUserId();
    }

    /// <summary>
    /// Gets the display name from claims only (name, then username, then email), or null. Cognito
    /// access tokens have none, so <see cref="UserProfileService"/> falls back to userInfo.
    /// </summary>
    /// <remarks>
    /// Checks both raw and mapped claim types, since MapInboundClaims renames "name" and "email".
    /// </remarks>
    public static string? TryGetDisplayNameFromClaims(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue("cognito:username")
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");
    }
}
