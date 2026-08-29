using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PuckDrop.Api.Auth;

/// <summary>
/// Development-only authentication handler that auto-authenticates all requests
/// as a dev user with admin privileges. Only active when neither Cognito nor Keycloak is
/// configured.
/// </summary>
public class DevAuthenticationHandler(
    IOptionsMonitor<DevAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<DevAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "dev-user"),
            new Claim("sub", "dev-user"),
            new Claim("name", "Dev User"),
            new Claim(ClaimTypes.Role, "admin")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class DevAuthenticationOptions : AuthenticationSchemeOptions { }
