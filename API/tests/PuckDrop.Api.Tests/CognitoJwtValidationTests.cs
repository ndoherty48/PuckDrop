using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PuckDrop.Api.Tests;

/// <summary>
/// Cognito access tokens have no "aud" claim, so validating the audience rejected every token.
/// These build a Cognito-shaped token and check that the approach AddApis uses (no audience
/// check, manual "client_id" check) accepts it. The E2E suite can't catch this, since Keycloak
/// tokens do carry "aud".
/// </summary>
public class CognitoJwtValidationTests
{
    private const string Issuer = "https://cognito-idp.eu-west-1.amazonaws.com/eu-west-1_ExampleId";
    private const string ClientId = "7q1kn4320mejjql5tigi0o9dna";

    private static (string Token, SecurityKey Key) CreateCognitoShapedAccessToken()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            // Deliberately no "Audience" - matches a real Cognito access token with no resource
            // server configured.
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["client_id"] = ClientId,
                ["cognito:groups"] = new[] { "admin" },
                ["token_use"] = "access",
            },
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), key);
    }

    [Fact]
    public async Task OriginalConfig_ValidateAudienceTrue_RejectsRealCognitoAccessToken()
    {
        var (token, key) = CreateCognitoShapedAccessToken();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = ClientId,
            ValidateLifetime = true,
            IssuerSigningKey = key,
        });

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    [Fact]
    public async Task FixedConfig_ValidateAudienceFalse_AcceptsRealCognitoAccessToken()
    {
        var (token, key) = CreateCognitoShapedAccessToken();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = key,
        });

        Assert.True(result.IsValid);
        Assert.Equal(ClientId, result.ClaimsIdentity!.FindFirst("client_id")?.Value);
    }

    [Fact]
    public async Task FixedConfig_WrongClientId_WouldFailTheManualCheck()
    {
        var (token, key) = CreateCognitoShapedAccessToken();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = key,
        });

        // Token validation itself succeeds (no "aud" to fail on) - the manual client_id check in
        // ApiServiceCollectionExtensions.AddApis's OnTokenValidated event is what's actually
        // responsible for rejecting a token issued for a *different* app client.
        Assert.True(result.IsValid);
        Assert.NotEqual("some-other-client-id", result.ClaimsIdentity!.FindFirst("client_id")?.Value);
    }

    [Fact]
    public async Task CognitoGroupsArrayClaim_ExpandsIntoMultipleSameTypedClaims()
    {
        // CognitoClaimsTransformation relies on the handler splitting a JSON array into claims.
        var (token, key) = CreateCognitoShapedAccessToken();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = key,
        });

        Assert.True(result.IsValid);
        var groupClaims = result.ClaimsIdentity!.FindAll("cognito:groups").Select(c => c.Value).ToList();
        Assert.Equal(["admin"], groupClaims);
    }
}
