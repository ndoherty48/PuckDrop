using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace PuckDrop.Web.Auth;

/// <summary>
/// Normalizes whichever identity provider is configured (Cognito or Keycloak - config fetched
/// from the API's <c>auth-config</c> endpoint at boot, see Program.cs) into standard
/// <see cref="ClaimTypes.Role"/> claims, so role-based authorization
/// (<c>[Authorize(Roles = "...")]</c>, <c>&lt;AuthorizeView Roles="..."&gt;</c>) works the same
/// way regardless of provider - the same normalization PuckDrop.Api does server-side via
/// <c>CognitoClaimsTransformation</c>/<c>KeycloakClaimsTransformation</c>. Switching providers
/// is then purely a server-side config change, not a code change.
/// </summary>
/// <remarks>
/// The Blazor OIDC pipeline merges claims from both the ID token and the userinfo endpoint
/// response, so a claim present in both arrives as a JSON array of duplicate values rather than
/// a single value - the extractors below tolerate that by flattening/deduplicating.
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

            // Blazor names the user from the "name" claim by default, but Cognito users often have
            // no name attribute set - fall back to the pool's required preferred_username, then
            // email. Same precedence as the API's display-name resolution (UserProfileService).
            if (string.IsNullOrEmpty(identity.Name))
            {
                var fallbackName = ExtractFirstString(account, "preferred_username")
                    ?? ExtractFirstString(account, "email");

                if (fallbackName is not null)
                    identity.AddClaim(new Claim(identity.NameClaimType, fallbackName));
            }
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

        return FlattenStrings(element)
            .Select(role => role.GetString())
            .Where(roleName => !string.IsNullOrEmpty(roleName))!;
    }

    /// <summary>
    /// The first non-empty string value of a claim, whether it arrived as a plain string or as a
    /// (possibly nested/duplicated - see class remarks) JSON array.
    /// </summary>
    private static string? ExtractFirstString(RemoteUserAccount account, string claimName)
    {
        if (!account.AdditionalProperties.TryGetValue(claimName, out var value))
            return null;

        return value switch
        {
            string s when !string.IsNullOrWhiteSpace(s) => s,
            JsonElement element => FlattenStrings(element)
                .Select(e => e.GetString())
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            _ => null
        };
    }

    private static IEnumerable<JsonElement> FlattenStrings(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.EnumerateArray().SelectMany(FlattenStrings),
        JsonValueKind.String => [e],
        _ => []
    };
}
