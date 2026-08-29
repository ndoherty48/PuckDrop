using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace PuckDrop.Web.Auth;

/// <summary>
/// Normalizes whichever identity provider is configured (Cognito or Keycloak - see
/// <c>wwwroot/appsettings.json</c>'s "Oidc" section) into standard <see cref="ClaimTypes.Role"/>
/// claims, so role-based authorization (<c>[Authorize(Roles = "...")]</c>,
/// <c>&lt;AuthorizeView Roles="..."&gt;</c>) works the same way regardless of provider - the
/// same normalization PuckDrop.Api does server-side via
/// <c>CognitoClaimsTransformation</c>/<c>KeycloakClaimsTransformation</c>. Switching providers
/// is then purely a config change (which authority/client ID is configured), not a code change.
/// </summary>
/// <remarks>
/// The Blazor OIDC pipeline merges claims from both the ID token and the userinfo endpoint
/// response, so a claim present in both arrives as a JSON array of duplicate values rather than
/// a single value - both extractors below tolerate that by flattening/deduplicating.
/// </remarks>
public class PuckDropClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
    : AccountClaimsPrincipalFactory<RemoteUserAccount>(accessor)
{
    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        RemoteUserAccount account, RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);

        if (account is not null && user.Identity is ClaimsIdentity identity)
        {
            var roleNames = ExtractKeycloakRealmRoles(account)
                .Concat(ExtractCognitoGroups(account))
                .Distinct();

            foreach (var roleName in roleNames)
                identity.AddClaim(new Claim(identity.RoleClaimType, roleName));
        }

        return user;
    }

    /// <summary>
    /// Keycloak puts realm roles in a "realm_access" claim shaped as
    /// <c>{"roles":["admin", ...]}</c> rather than as individual role claims.
    /// </summary>
    private static IEnumerable<string> ExtractKeycloakRealmRoles(RemoteUserAccount account)
    {
        if (!account.AdditionalProperties.TryGetValue("realm_access", out var value) ||
            value is not JsonElement element)
            return [];

        JsonElement[] realmAccessObjects = element.ValueKind switch
        {
            JsonValueKind.Object => [element],
            JsonValueKind.Array => element.EnumerateArray().ToArray(),
            _ => []
        };

        return realmAccessObjects
            .Where(o => o.ValueKind == JsonValueKind.Object)
            .SelectMany(o => o.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray()
                : [])
            .Select(role => role.GetString())
            .Where(roleName => !string.IsNullOrEmpty(roleName))!;
    }

    /// <summary>
    /// Cognito puts group membership in a "cognito:groups" claim shaped as a (possibly
    /// nested/duplicated - see class remarks) JSON array of group name strings.
    /// </summary>
    private static IEnumerable<string> ExtractCognitoGroups(RemoteUserAccount account)
    {
        if (!account.AdditionalProperties.TryGetValue("cognito:groups", out var value) ||
            value is not JsonElement element)
            return [];

        return Flatten(element)
            .Select(role => role.GetString())
            .Where(roleName => !string.IsNullOrEmpty(roleName))!;

        static IEnumerable<JsonElement> Flatten(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Array => e.EnumerateArray().SelectMany(Flatten),
            JsonValueKind.String => [e],
            _ => []
        };
    }
}
