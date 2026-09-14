namespace PuckDrop.Application.Services.Abstractions;

/// <summary>
/// Resolves profile details for the current authenticated user that aren't reliably present in
/// the token the API receives - implemented in PuckDrop.Api, which knows the active identity
/// provider and the current request.
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// Gets the current user's display name, falling back to their user ID when no name can be
    /// resolved. Never throws for a lookup failure - an answer submission shouldn't fail just
    /// because a friendly name couldn't be found.
    /// </summary>
    Task<string> GetDisplayNameAsync(CancellationToken cancellationToken = default);
}
