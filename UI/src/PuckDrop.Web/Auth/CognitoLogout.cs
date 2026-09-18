using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace PuckDrop.Web.Auth;

/// <summary>
/// Builds the logout request for Cognito, whose <c>/logout</c> redirects to <c>/login</c> unless
/// it gets <c>client_id</c> and <c>logout_uri</c>.
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
        // oidc-client appends "extraQueryParams" to the logout URL only. It must be a JsonElement:
        // Blazor's source-generated serializer can't handle a Dictionary.
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
