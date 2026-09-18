using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Maps Keycloak's <c>realm_access</c> admin role to the standard "admin" role claim - see
/// <see cref="CognitoClaimsTransformation"/> for Cognito.
/// </summary>
/// <remarks>
/// <c>realm_access</c> is a JSON object (<c>{"roles":[...]}</c>), which the JWT handler doesn't
/// split, so it's parsed here.
/// </remarks>
public class KeycloakClaimsTransformation(KeycloakSettings settings) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || principal.HasClaim(identity.RoleClaimType, "admin"))
            return Task.FromResult(principal);

        var realmAccess = principal.FindFirstValue("realm_access");
        if (realmAccess is not null)
        {
            using var doc = JsonDocument.Parse(realmAccess);
            var isAdmin = doc.RootElement.TryGetProperty("roles", out var roles) &&
                roles.ValueKind == JsonValueKind.Array &&
                roles.EnumerateArray().Any(r => r.GetString() == settings.AdminRoleName);

            if (isAdmin)
                identity.AddClaim(new Claim(identity.RoleClaimType, "admin"));
        }

        return Task.FromResult(principal);
    }
}
