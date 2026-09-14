using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace PuckDrop.Web.Auth;

/// <summary>
/// Builds the logout request for Cognito, whose <c>/logout</c> endpoint ignores the standard OIDC
/// <c>id_token_hint</c>/<c>post_logout_redirect_uri</c> parameters and bounces to its <c>/login</c>
/// page unless it also gets <c>client_id</c> and <c>logout_uri</c> (confirmed live).
/// </summary>
public static class CognitoLogout
{
    public const string LogoutPath = "authentication/logout";
    public const string LoggedOutPath = "authentication/logged-out";

    public static InteractiveRequestOptions CreateRequest(string clientId, string baseUri, string logoutUri)
    {
        var request = new InteractiveRequestOptions
        {
            Interaction = InteractionType.SignOut,
            ReturnUrl = baseUri
        };
        // Blazor spreads additional request parameters onto the OIDC library's signoutRedirect
        // arguments, and the library appends an "extraQueryParams" object to the end-session URL -
        // so nesting both under that name puts them on the logout request only, never on authorize.
        //
        // The value has to be a JsonElement: NavigateToLogin serializes the request through Blazor's
        // source-generated InteractiveRequestOptionsSerializerContext, which has metadata for
        // JsonElement but not for a Dictionary<string, string> (that threw NotSupportedException
        // on click - see CognitoLogoutTests).
        request.TryAddAdditionalParameter("extraQueryParams", CreateExtraQueryParams(clientId, logoutUri));
        return request;
    }

    private static JsonElement CreateExtraQueryParams(string clientId, string logoutUri)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("client_id", clientId);
            writer.WriteString("logout_uri", logoutUri);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
