namespace PuckDrop.Web.Services;

/// <summary>
/// Outcome of Program.cs's early fetch of OIDC config from the API's auth-config endpoint,
/// registered as a singleton before the app renders so App.razor can show a clear message
/// instead of a blank page if the API couldn't be reached at boot (rather than the app trying to
/// render its normal auth-dependent component tree against config that never loaded).
/// </summary>
public record AuthConfigLoadResult(bool Success, string? ErrorMessage = null)
{
    public static readonly AuthConfigLoadResult Ok = new(true);
}
