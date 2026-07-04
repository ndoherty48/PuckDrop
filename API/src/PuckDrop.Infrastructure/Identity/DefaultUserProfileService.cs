using PuckDrop.Application.Services.Abstractions;

namespace PuckDrop.Infrastructure.Identity;

/// <summary>
/// Placeholder user profile service. Returns the userId as the display name.
/// Will be replaced by a Cognito-backed implementation in Phase 5.
/// </summary>
public class DefaultUserProfileService : IUserProfileService
{
    public string GetDisplayName(string userId) => userId;
}
