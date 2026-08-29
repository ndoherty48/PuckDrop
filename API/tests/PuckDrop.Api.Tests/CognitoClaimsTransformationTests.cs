using System.Security.Claims;
using PuckDrop.Api.Auth;
using Xunit;

namespace PuckDrop.Api.Tests;

public class CognitoClaimsTransformationTests
{
    private static readonly CognitoSettings Settings = new()
    {
        UserPoolId = "eu-west-1_abc123",
        ClientId = "client-1",
        Region = "eu-west-1"
        // AdminGroupName defaults to "admin"
    };

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims));

    [Fact]
    public async Task TransformAsync_MatchingAdminGroupClaim_AddsRoleClaim()
    {
        var principal = PrincipalWith(new Claim("cognito:groups", "admin"));

        var result = await new CognitoClaimsTransformation(Settings).TransformAsync(principal);

        Assert.True(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_NonMatchingGroupClaim_DoesNotAddRoleClaim()
    {
        var principal = PrincipalWith(new Claim("cognito:groups", "friends"));

        var result = await new CognitoClaimsTransformation(Settings).TransformAsync(principal);

        Assert.False(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_MultipleGroupClaims_MatchesAnyOfThem()
    {
        // Cognito's JSON array claim gets expanded into multiple same-typed claims by the JWT
        // bearer handler - simulate that shape directly rather than a single combined value.
        var principal = PrincipalWith(
            new Claim("cognito:groups", "friends"),
            new Claim("cognito:groups", "admin"));

        var result = await new CognitoClaimsTransformation(Settings).TransformAsync(principal);

        Assert.True(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_NoGroupsClaimAtAll_DoesNotAddRoleClaim()
    {
        var principal = PrincipalWith();

        var result = await new CognitoClaimsTransformation(Settings).TransformAsync(principal);

        Assert.False(result.IsInRole("admin"));
    }

    [Fact]
    public async Task TransformAsync_AlreadyHasAdminRoleClaim_DoesNotDuplicateIt()
    {
        var principal = PrincipalWith(
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("cognito:groups", "admin"));

        var result = await new CognitoClaimsTransformation(Settings).TransformAsync(principal);

        Assert.Single(result.FindAll(ClaimTypes.Role));
    }
}
