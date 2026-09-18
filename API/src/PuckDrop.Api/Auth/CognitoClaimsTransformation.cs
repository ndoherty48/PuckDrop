using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Maps Cognito's <c>cognito:groups</c> admin group to the standard "admin" role claim - see
/// <see cref="KeycloakClaimsTransformation"/> for Keycloak.
/// </summary>
/// <remarks>
/// The JWT handler already splits the JSON array into one claim per group, so no parsing needed.
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
