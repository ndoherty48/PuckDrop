namespace PuckDrop.Api.Auth;

public class KeycloakSettings
{
    public static string SectionName => "Keycloak";
    
    public required string ServerUrl { get; set; } // e.g., "https://keycloak.example.com"
    public required string Realm { get; set; }
    public required string ClientId { get; set; }
    public string AdminRoleName { get; set; }  = "admin";
}
