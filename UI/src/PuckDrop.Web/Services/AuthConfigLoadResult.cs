namespace PuckDrop.Web.Services;

/// <summary>
/// Whether Program.cs loaded the OIDC config, so App.razor can show an error instead of a blank
/// page.
/// </summary>
public record AuthConfigLoadResult(bool Success, string? ErrorMessage = null)
{
    public static readonly AuthConfigLoadResult Ok = new(true);
}
