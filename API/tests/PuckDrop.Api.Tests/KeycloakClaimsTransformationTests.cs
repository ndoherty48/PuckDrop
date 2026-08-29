using System.Security.Claims;
using System.Text.Json;
using PuckDrop.Api.Auth;
using Xunit;

namespace PuckDrop.Api.Tests;

public class KeycloakClaimsTransformationTests
{
    private static readonly KeycloakSettings Settings = new()
    {
        ServerUrl = "https://keycloak.example.com",
        Realm = "PuckDrop",
        ClientId = "PuckDrop-API"
        // AdminRoleName defaults to "admin"
    };

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims));

    [Fact]
    public async Task TransformAsync_RealmAccessWithAdminRole_AddsRoleClaim()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["admin","offline_access"]}"""));

        var result = await new KeycloakClaimsTransformation(Settings).TransformAsync(principal);

        Assert.True(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_RealmAccessWithoutAdminRole_DoesNotAddRoleClaim()
    {
        var principal = PrincipalWith(new Claim("realm_access", """{"roles":["offline_access"]}"""));

        var result = await new KeycloakClaimsTransformation(Settings).TransformAsync(principal);

        Assert.False(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_NoRealmAccessClaimAtAll_DoesNotAddRoleClaim()
    {
        var principal = PrincipalWith();

        var result = await new KeycloakClaimsTransformation(Settings).TransformAsync(principal);

        Assert.False(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_RealmAccessMissingRolesProperty_DoesNotAddRoleClaimOrThrow()
    {
        var principal = PrincipalWith(new Claim("realm_access", "{}"));

        var result = await new KeycloakClaimsTransformation(Settings).TransformAsync(principal);

        Assert.False(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_AlreadyHasAdminRoleClaim_ShortCircuitsWithoutParsing()
    {
        // Malformed JSON would throw if parsed - the fact this doesn't throw confirms the
        // already-has-the-claim short-circuit runs before FindFirstValue/JsonDocument.Parse.
        var principal = PrincipalWith(
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("realm_access", "not valid json"));

        var result = await new KeycloakClaimsTransformation(Settings).TransformAsync(principal);

        Assert.Single(result.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task TransformAsync_MalformedRealmAccessJson_ThrowsJsonException()
    {
        // Pinning down actual current behavior rather than leaving it undocumented: malformed
        // JSON in a claim isn't caught anywhere in the transformation, so it propagates as an
        // uncaught exception (JsonDocument.Parse throws the JsonReaderException subclass of
        // JsonException for this input). Untrusted-token-shape hardening is a candidate
        // follow-up, not assumed/fixed here.
        var principal = PrincipalWith(new Claim("realm_access", "not valid json"));

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            new KeycloakClaimsTransformation(Settings).TransformAsync(principal));
    }
}
