using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Normalizes Keycloak's <c>realm_access</c> claim into a standard <see cref="ClaimTypes.Role"/>
/// claim, so downstream authorization (the shared <c>AdminPolicy</c> in
/// <see cref="ApiServiceCollectionExtensions"/>, controllers) never needs to know which identity
/// provider issued the token - see <see cref="CognitoClaimsTransformation"/> for the Cognito
/// equivalent.
/// </summary>
/// <remarks>
/// Keycloak puts realm roles in a "realm_access" claim shaped as
/// <c>{"roles":["admin", ...]}</c> rather than as individual role claims, and - unlike a JSON
/// array claim - the JWT bearer handler does not expand a JSON object claim into multiple
/// claims, so this has to parse it explicitly.
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
