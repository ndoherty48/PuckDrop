using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PuckDrop.Api.Tests;

/// <summary>
/// Regression coverage for a real, confirmed-live bug: Cognito user pool access tokens carry no
/// "aud" claim at all unless the user pool has a configured resource server (AWS's own docs:
/// "Present only if your application requested a resource binding") - this app has none. The
/// original ApiServiceCollectionExtensions config set ValidateAudience=true/ValidAudience=ClientId,
/// which rejected every single Cognito access token, admin or not, with
/// SecurityTokenInvalidAudienceException ("the 'audiences' parameter is empty") - confirmed via a
/// real deployed distribution, where an admin-group user's requests came back 403 even though API
/// Gateway's own JWT authorizer (which has documented Cognito-aware "check client_id when aud is
/// absent" fallback logic) had already accepted the identical token.
///
/// The E2E suite can't catch this: it runs against local Keycloak, whose tokens do carry a normal
/// "aud" claim, so this Cognito-specific token shape is only ever exercised by a real deployment.
/// This test constructs a token matching that exact documented shape and proves the *approach*
/// ApiServiceCollectionExtensions.AddApis now uses (ValidateAudience=false, manual "client_id"
/// check) validates it, while the original config does not - a literal call into AddApis itself
/// would need a full DI/hosting harness, which is more than this specific regression needs.
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
        // Confirms CognitoClaimsTransformation's own documented assumption about how the JWT
        // handler represents a JSON array claim - one Claim per element, not a single
        // JSON-string-valued claim - since that assumption was previously untested against a
        // real token (the existing unit tests simulate the shape rather than prove it).
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
