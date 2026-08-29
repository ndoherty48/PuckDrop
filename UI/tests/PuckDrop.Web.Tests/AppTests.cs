using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Web.Services;
using Xunit;

namespace PuckDrop.Web.Tests;

/// <summary>
/// Covers App.razor's failure-mode branch: if Program.cs's early auth-config fetch failed,
/// AuthenticationStateProvider/etc. were never registered (see Program.cs), so App.razor must
/// check AuthConfigLoadResult before rendering its normal auth-dependent tree - otherwise it'd
/// throw trying to resolve a service that doesn't exist, instead of showing a clear message.
/// </summary>
public class AppTests : BunitContext
{
    [Fact]
    public void AuthConfigLoadFailed_RendersErrorMessage_NotTheNormalRouterTree()
    {
        Services.AddSingleton(new AuthConfigLoadResult(false, "Could not reach the server: timed out"));

        var cut = Render<PuckDrop.Web.App>();

        Assert.Contains("Couldn't reach the server", cut.Markup);
        Assert.Contains("Could not reach the server: timed out", cut.Markup);
    }

    // The success path (AuthConfigLoadResult.Ok) isn't separately bUnit-tested here: rendering
    // <App/> all the way through requires the full OIDC RemoteAuthenticationService machinery
    // registered (AddOidcAuthentication, normally called by Program.cs only in that branch) -
    // standing that up just to prove a two-line @if/@else takes its else branch is disproportionate
    // relative to what it'd add. It's exercised implicitly by every other bUnit test in this
    // project (PollTests, LeaderboardTests, Admin/*Tests), which all successfully render real
    // pages/components.
}
