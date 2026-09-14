using System.Security.Claims;
using PuckDrop.Api.Auth;
using Xunit;

namespace PuckDrop.Api.Tests;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims));

    // ─── GetUserId ──────────────────────────────────────────────────────────

    [Fact]
    public void GetUserId_NameIdentifierClaimPresent_ReturnsItsValue()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "user-abc"));

        Assert.Equal("user-abc", principal.GetUserId());
    }

    [Fact]
    public void GetUserId_OnlyRawSubClaimPresent_FallsBackToSub()
    {
        var principal = PrincipalWith(new Claim("sub", "user-abc"));

        Assert.Equal("user-abc", principal.GetUserId());
    }

    [Fact]
    public void GetUserId_NeitherClaimPresent_ThrowsUnauthorizedAccessException()
    {
        var principal = PrincipalWith();

        Assert.Throws<UnauthorizedAccessException>(() => principal.GetUserId());
    }

    // ─── GetDisplayName ─────────────────────────────────────────────────────

    [Fact]
    public void GetDisplayName_NameClaimPresent_ReturnsItsValue()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Name, "Nathan"));

        Assert.Equal("Nathan", principal.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_OnlyPreferredUsernamePresent_FallsBackToIt()
    {
        // Keycloak's standard username claim; not affected by JwtBearerOptions'
        // MapInboundClaims remapping, unlike "name"/"email".
        var principal = PrincipalWith(new Claim("preferred_username", "nathan.d"));

        Assert.Equal("nathan.d", principal.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_OnlyEmailPresent_FallsBackToEmail()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Email, "nathan@example.com"));

        Assert.Equal("nathan@example.com", principal.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_NothingButSubPresent_FallsAllTheWayBackToUserId()
    {
        var principal = PrincipalWith(new Claim("sub", "user-abc"));

        Assert.Equal("user-abc", principal.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_NoUsableClaimsAtAll_ThrowsUnauthorizedAccessException()
    {
        // The final fallback is GetUserId(), which throws when even "sub" is missing.
        var principal = PrincipalWith();

        Assert.Throws<UnauthorizedAccessException>(() => principal.GetDisplayName());
    }

    // ─── TryGetDisplayNameFromClaims ────────────────────────────────────────

    [Fact]
    public void TryGetDisplayNameFromClaims_OnlySubPresent_ReturnsNull()
    {
        // The shape of a Cognito access token as far as names go - no name, username, or email.
        var principal = PrincipalWith(new Claim("sub", "user-abc"));

        Assert.Null(principal.TryGetDisplayNameFromClaims());
    }

    [Fact]
    public void TryGetDisplayNameFromClaims_PreferredUsernamePresent_ReturnsIt()
    {
        var principal = PrincipalWith(new Claim("sub", "user-abc"), new Claim("preferred_username", "nathan.d"));

        Assert.Equal("nathan.d", principal.TryGetDisplayNameFromClaims());
    }
}
