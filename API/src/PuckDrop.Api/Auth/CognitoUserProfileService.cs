using PuckDrop.Application.Services.Abstractions;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Resolves display names from the current HTTP context's JWT claims.
/// For users other than the current authenticated user, falls back to the userId.
/// The leaderboard stores display names on first entry creation.
/// </summary>
public class CognitoUserProfileService(IHttpContextAccessor httpContextAccessor) : IUserProfileService
{
    public string GetDisplayName(string userId)
    {
        var currentUser = httpContextAccessor.HttpContext?.User;
        if (currentUser is not null)
        {
            var currentUserId = currentUser.FindFirst("sub")?.Value;
            if (currentUserId == userId)
            {
                return currentUser.GetDisplayName();
            }
        }

        return userId;
    }
}
