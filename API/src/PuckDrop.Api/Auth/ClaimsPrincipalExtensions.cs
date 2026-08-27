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
    /// <remarks>
    /// JwtBearerOptions.MapInboundClaims defaults to true, which remaps standard JWT claim
    /// types (e.g. "name" -> ClaimTypes.Name, "email" -> ClaimTypes.Email) before this ever
    /// runs - so both the raw and the mapped claim type need checking. "preferred_username"
    /// is Keycloak's standard username claim and isn't affected by that remapping.
    /// </remarks>
    public static string GetDisplayName(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue("cognito:username")
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? principal.GetUserId();
    }
}
