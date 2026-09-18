using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace PuckDrop.Web.Auth;

/// <summary>
/// Maps Keycloak and Cognito admin claims to standard role claims, like the API's claims
/// transformations, and falls back to a username or email for the display name.
/// </summary>
/// <remarks>
/// Blazor merges ID token and userinfo claims, so a claim in both arrives as an array of
/// duplicates; the extractors below flatten and deduplicate.
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

            // Cognito users often have no "name"; same precedence as the API's UserProfileService.
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
    /// Cognito puts groups in a "cognito:groups" array of names (possibly nested - see remarks).
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
    /// The first non-empty string of a claim, whether a plain string or a (nested) array.
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
