namespace PuckDrop.Domain.Services;

/// <summary>
/// Resolves user profile information (display names, etc.) from the identity provider.
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// Gets the display name for a user ID.
    /// </summary>
    string GetDisplayName(string userId);
}
