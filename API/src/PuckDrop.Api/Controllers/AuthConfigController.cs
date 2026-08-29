using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Mappings;

namespace PuckDrop.Api.Controllers;

/// <summary>
/// Serves the OIDC config the Blazor WASM app needs to log in - deliberately unauthenticated
/// (chicken-and-egg: the app can't have a token yet when it's asking how to get one), and only
/// returns values that are meant to be public anyway (an OIDC authority URL and a public client
/// ID, no secret). In production this needs its own API Gateway route with no JWT authorizer -
/// see DeploymentStack.cs - since the catch-all Lambda route otherwise requires a token for
/// every path under /puckdrop/.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("auth-config")]
public class AuthConfigController(AuthDiscoveryOptions authDiscoveryOptions) : ControllerBase
{
    [HttpGet]
    public ActionResult<AuthConfigResponse> GetAuthConfig() => Ok(authDiscoveryOptions.ToResponse());
}
