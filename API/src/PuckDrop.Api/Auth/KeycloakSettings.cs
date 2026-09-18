namespace PuckDrop.Api.Auth;

public class KeycloakSettings
{
    public static string SectionName => "Keycloak";
    
    public required string ServerUrl { get; set; } // e.g., "https://keycloak.example.com"
    public required string Realm { get; set; }

    /// <summary>
    /// The API's client, used only for audience validation (e.g. "PuckDrop-API").
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// The client the Blazor app logs in with (e.g. "PuckDrop-UI"), served via
    /// <see cref="AuthDiscoveryOptions"/>.
    /// </summary>
    public required string UiClientId { get; set; }

    public string AdminRoleName { get; set; }  = "admin";
}
