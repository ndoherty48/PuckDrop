using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Auth;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// NavigateToLogin serializes the request into the history entry state through Blazor's own
/// source-generated InteractiveRequestOptionsSerializerContext, which only has metadata for a few
/// types - an unsupported additional-parameter value throws NotSupportedException at click time
/// (seen live: a Dictionary&lt;string, string&gt; did exactly that). Going through the real
/// NavigateToLogin here exercises that same serialization path.
/// </summary>
public class CognitoLogoutTests : BunitContext
{
    [Fact]
    public void CreateRequest_SerializesIntoNavigationState_WithClientIdAndLogoutUri()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        var request = CognitoLogout.CreateRequest(
            "client123", "https://example.cloudfront.net/", "https://example.cloudfront.net/authentication/logged-out");

        nav.NavigateToLogin(CognitoLogout.LogoutPath, request);

        var state = nav.History.First().Options.HistoryEntryState;
        Assert.NotNull(state);
        using var doc = JsonDocument.Parse(state);
        Assert.Equal(nameof(InteractionType.SignOut), doc.RootElement.GetProperty("interaction").GetString(), ignoreCase: true);
        var extraQueryParams = doc.RootElement.GetProperty("additionalRequestParameters").GetProperty("extraQueryParams");
        Assert.Equal("client123", extraQueryParams.GetProperty("client_id").GetString());
        Assert.Equal("https://example.cloudfront.net/authentication/logged-out", extraQueryParams.GetProperty("logout_uri").GetString());

        // The logout page reads this state back (via the type's own [JsonConverter]) before
        // passing the request on to the OIDC library - make sure it survives the round trip.
        var roundTripped = JsonSerializer.Deserialize<InteractiveRequestOptions>(state)!;
        Assert.Equal(InteractionType.SignOut, roundTripped.Interaction);
        Assert.True(roundTripped.TryGetAdditionalParameter<JsonElement>("extraQueryParams", out var roundTrippedParams));
        Assert.Equal("client123", roundTrippedParams.GetProperty("client_id").GetString());
    }
}
