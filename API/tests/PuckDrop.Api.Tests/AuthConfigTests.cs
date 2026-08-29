using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Controllers;
using PuckDrop.Api.Mappings;
using Xunit;

namespace PuckDrop.Api.Tests;

public class AuthConfigMappingExtensionsTests
{
    [Fact]
    public void ToResponse_MapsAllThreeFields()
    {
        var options = new AuthDiscoveryOptions("https://keycloak.example.com/realms/PuckDrop", "PuckDrop-UI", "code");

        var response = options.ToResponse();

        Assert.Equal(options.Authority, response.Authority);
        Assert.Equal(options.ClientId, response.ClientId);
        Assert.Equal(options.ResponseType, response.ResponseType);
    }
}

public class AuthConfigControllerTests
{
    [Fact]
    public void GetAuthConfig_ReturnsTheRegisteredDiscoveryOptions()
    {
        var options = new AuthDiscoveryOptions("https://keycloak.example.com/realms/PuckDrop", "PuckDrop-UI", "code");
        var controller = new AuthConfigController(options);

        var result = controller.GetAuthConfig();

        var response = Assert.IsType<AuthConfigResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("https://keycloak.example.com/realms/PuckDrop", response.Authority);
        Assert.Equal("PuckDrop-UI", response.ClientId);
        Assert.Equal("code", response.ResponseType);
    }
}
