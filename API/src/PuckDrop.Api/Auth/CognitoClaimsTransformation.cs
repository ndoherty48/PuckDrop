using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Normalizes Cognito's <c>cognito:groups</c> claim into a standard
/// <see cref="ClaimTypes.Role"/> claim, so downstream authorization (the shared
/// <c>AdminPolicy</c> in <see cref="ApiServiceCollectionExtensions"/>, controllers) never needs
/// to know which identity provider issued the token - see
/// <see cref="KeycloakClaimsTransformation"/> for the Keycloak equivalent.
/// </summary>
/// <remarks>
/// Cognito emits group membership as a JSON array under "cognito:groups". The JWT bearer
/// handler expands a JSON array claim value into multiple claims of the same type (one per
/// element) rather than a single JSON-string claim, so a plain equality check against each
/// claim's value is sufficient here - no JSON parsing needed (contrast with Keycloak, whose
/// "realm_access" claim is a JSON *object*, which is not expanded).
/// </remarks>
public class CognitoClaimsTransformation(CognitoSettings settings) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is ClaimsIdentity identity &&
            !principal.HasClaim(identity.RoleClaimType, "admin") &&
            principal.HasClaim(c => c.Type == "cognito:groups" && c.Value == settings.AdminGroupName))
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, "admin"));
        }

        return Task.FromResult(principal);
    }
}
