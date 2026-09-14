using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using PuckDrop.Web.Auth;
using Xunit;

namespace PuckDrop.Web.Tests;

public class PuckDropClaimsPrincipalFactoryTests
{
    /// <summary>
    /// AccountClaimsPrincipalFactory's base CreateUserAsync doesn't touch the token provider
    /// while building claims from RemoteUserAccount.AdditionalProperties (that's only needed for
    /// token refresh flows), so a throwing fake is enough to prove that at test time.
    /// </summary>
    private sealed class ThrowingAccessTokenProviderAccessor : IAccessTokenProviderAccessor
    {
        public IAccessTokenProvider TokenProvider => throw new NotSupportedException("Not needed for claims construction.");
    }

    private static readonly PuckDropClaimsPrincipalFactory Factory = new(new ThrowingAccessTokenProviderAccessor());

    private static RemoteUserAccount AccountWith(string propertyName, JsonElement value)
    {
        // RemoteUserAccount.AdditionalProperties isn't auto-initialized by the parameterless
        // constructor (it's only populated by the real OIDC deserialization path) - both this
        // helper and the base factory itself NullReferenceException without an explicit
        // dictionary, as building an empty-account test below confirmed.
        var account = new RemoteUserAccount { AdditionalProperties = new Dictionary<string, object>() };
        account.AdditionalProperties[propertyName] = value;
        return account;
    }

    private static RemoteUserAccount EmptyAccount() =>
        new() { AdditionalProperties = new Dictionary<string, object>() };

    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

    // ─── Keycloak realm_access ──────────────────────────────────────────────

    [Fact]
    public async Task CreateUserAsync_RealmAccessAsSingleObject_AddsAdminRole()
    {
        var account = AccountWith("realm_access", ParseJson("""{"roles":["admin","offline_access"]}"""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.True(user.IsInRole("admin"));
    }

    [Fact]
    public async Task CreateUserAsync_RealmAccessAsArrayOfDuplicateObjects_StillAddsAdminRole()
    {
        // The Blazor OIDC pipeline merges ID-token + userinfo claims, so a claim present in both
        // arrives as a JSON array of duplicate objects rather than a single object.
        var account = AccountWith("realm_access", ParseJson("""[{"roles":["admin"]},{"roles":["admin"]}]"""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.True(user.IsInRole("admin"));
        Assert.Single(user.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task CreateUserAsync_RealmAccessWithoutAdminRole_DoesNotAddAdminRole()
    {
        var account = AccountWith("realm_access", ParseJson("""{"roles":["offline_access"]}"""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.False(user.IsInRole("admin"));
    }

    // ─── Cognito cognito:groups ─────────────────────────────────────────────

    [Fact]
    public async Task CreateUserAsync_CognitoGroupsAsFlatArray_AddsAdminRole()
    {
        var account = AccountWith("cognito:groups", ParseJson("""["admin","friends"]"""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.True(user.IsInRole("admin"));
    }

    [Fact]
    public async Task CreateUserAsync_CognitoGroupsAsNestedDuplicatedArray_FlattensAndDeduplicates()
    {
        // Same ID-token/userinfo merge concern as realm_access, but cognito:groups is an array of
        // strings rather than an array of objects, so the duplication shows up as nested arrays.
        var account = AccountWith("cognito:groups", ParseJson("""[["admin"],["admin"]]"""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.True(user.IsInRole("admin"));
        Assert.Single(user.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task CreateUserAsync_NoRoleClaimsAtAll_HasNoRoles()
    {
        var user = await Factory.CreateUserAsync(EmptyAccount(), new RemoteAuthenticationUserOptions());

        Assert.False(user.IsInRole("admin"));
    }

    // ─── Display name fallback ──────────────────────────────────────────────

    [Fact]
    public async Task CreateUserAsync_NameClaimPresent_UsesName()
    {
        var account = EmptyAccount();
        account.AdditionalProperties["name"] = ParseJson("\"Nathan Doherty\"");
        account.AdditionalProperties["preferred_username"] = ParseJson("\"nathan\"");

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.Equal("Nathan Doherty", user.Identity?.Name);
    }

    [Fact]
    public async Task CreateUserAsync_OnlyPreferredUsername_FallsBackToIt()
    {
        // Cognito users often have no "name" attribute, but the pool requires preferred_username.
        var account = AccountWith("preferred_username", ParseJson("\"nathan\""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.Equal("nathan", user.Identity?.Name);
    }

    [Fact]
    public async Task CreateUserAsync_OnlyEmail_FallsBackToEmail()
    {
        var account = AccountWith("email", ParseJson("\"nathan@example.com\""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.Equal("nathan@example.com", user.Identity?.Name);
    }

    [Fact]
    public async Task CreateUserAsync_PreferredUsernameAsDuplicatedArray_UsesFirstValue()
    {
        // Same ID-token/userinfo merge concern as the role claims above.
        var account = AccountWith("preferred_username", ParseJson("""["nathan","nathan"]"""));

        var user = await Factory.CreateUserAsync(account, new RemoteAuthenticationUserOptions());

        Assert.Equal("nathan", user.Identity?.Name);
    }
}
