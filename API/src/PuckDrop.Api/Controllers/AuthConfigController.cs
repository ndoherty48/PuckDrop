using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;

namespace PuckDrop.Api.Controllers;

/// <summary>
/// Serves the UI's OIDC config. Unauthenticated because the app needs it before it has a token,
/// and it holds nothing secret. Production gives it its own no-auth route in DeploymentStack.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("auth-config")]
public class AuthConfigController(AuthDiscoveryOptions authDiscoveryOptions) : ControllerBase
{
    [HttpGet]
    public ActionResult<AuthConfigResponse> GetAuthConfig() => Ok(authDiscoveryOptions.ToResponse());
}
