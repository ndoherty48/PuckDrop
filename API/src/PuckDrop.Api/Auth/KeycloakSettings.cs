namespace PuckDrop.Api.Auth;

public class KeycloakSettings
{
    public static string SectionName => "Keycloak";
    
    public required string ServerUrl { get; set; } // e.g., "https://keycloak.example.com"
    public required string Realm { get; set; }

    /// <summary>
    /// The API's own client, used only for JWT audience validation (e.g. "PuckDrop-API"). NOT
    /// the client the browser logs in with - see <see cref="UiClientId"/> for that. Unlike
    /// Cognito (one UserPoolClient serves both purposes), Keycloak's realm has two separate
    /// clients here.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// The browser-facing OIDC client the Blazor WASM app logs in with (e.g. "PuckDrop-UI").
    /// Served to the UI via <see cref="AuthDiscoveryOptions"/>/<see cref="PuckDrop.Api.Controllers.AuthConfigController"/>.
    /// </summary>
    public required string UiClientId { get; set; }

    public string AdminRoleName { get; set; }  = "admin";
}
