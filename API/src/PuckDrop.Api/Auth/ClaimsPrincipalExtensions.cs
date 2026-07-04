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
    /// Gets the authenticated user's display name from Cognito claims.
    /// Falls back to username, then email, then user ID.
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue("name")
            ?? principal.FindFirstValue("cognito:username")
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.GetUserId();
    }
}
