using System.Security.Claims;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace PuckDrop.Web.Auth;

/// <summary>
/// Keycloak puts realm roles in a "realm_access" claim shaped as
/// <c>{"roles":["admin", ...]}</c> rather than as individual role claims, so
/// <see cref="ClaimsPrincipal.IsInRole"/> / <c>[Authorize(Roles = "...")]</c> /
/// <c>&lt;AuthorizeView Roles="..."&gt;</c> see nothing to match against by default.
/// This factory unpacks that claim into normal <see cref="ClaimTypes.Role"/> claims
/// so role-based authorization works the same way it does on the API
/// (see PuckDrop.Api.ApiServiceCollectionExtensions.AdminPolicy).
/// </summary>
/// <remarks>
/// The Blazor OIDC pipeline merges claims from both the ID token and the userinfo
/// endpoint response. Since both carry a "realm_access" claim, same-named claims get
/// combined into a JSON array of duplicate {"roles":[...]} objects rather than a single
/// object - so this handles "realm_access" as either a lone object or an array of them.
/// </remarks>
public class KeycloakAccountClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
    : AccountClaimsPrincipalFactory<RemoteUserAccount>(accessor)
{
    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        RemoteUserAccount account, RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);

        if (account is not null &&
            account.AdditionalProperties.TryGetValue("realm_access", out var realmAccess) &&
            realmAccess is JsonElement realmAccessElement &&
            user.Identity is ClaimsIdentity identity)
        {
            IEnumerable<JsonElement> realmAccessObjects = realmAccessElement.ValueKind switch
            {
                JsonValueKind.Object => [realmAccessElement],
                JsonValueKind.Array => realmAccessElement.EnumerateArray().ToArray(),
                _ => []
            };

            var roleNames = realmAccessObjects
                .Where(o => o.ValueKind == JsonValueKind.Object)
                .SelectMany(o => o.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                    ? roles.EnumerateArray()
                    : [])
                .Select(role => role.GetString())
                .Where(roleName => !string.IsNullOrEmpty(roleName))
                .Distinct();

            foreach (var roleName in roleNames)
                identity.AddClaim(new Claim(identity.RoleClaimType, roleName!));
        }

        return user;
    }
}
