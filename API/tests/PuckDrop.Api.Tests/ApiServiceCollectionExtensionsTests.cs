using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Api;
using PuckDrop.Api.Auth;
using Xunit;

namespace PuckDrop.Api.Tests;

public class ApiServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddApis_NeitherProviderConfigured_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var config = BuildConfig([]);

        Assert.Throws<InvalidOperationException>(() => services.AddApis(config));
    }

    [Fact]
    public void AddApis_CognitoConfigured_RegistersAuthDiscoveryOptionsUsingCognitoClientId()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cognito:UserPoolId"] = "eu-west-1_abc123",
            ["Cognito:ClientId"] = "cognito-client",
            ["Cognito:Region"] = "eu-west-1"
        });

        services.AddApis(config);
        var options = services.BuildServiceProvider().GetRequiredService<AuthDiscoveryOptions>();

        Assert.Equal("https://cognito-idp.eu-west-1.amazonaws.com/eu-west-1_abc123", options.Authority);
        Assert.Equal("cognito-client", options.ClientId);
        Assert.Equal("code", options.ResponseType);
    }

    [Fact]
    public void AddApis_KeycloakConfigured_RegistersAuthDiscoveryOptionsUsingUiClientIdNotApiClientId()
    {
        // The regression this guards against: the server's own audience-validation client
        // (ClientId, "PuckDrop-API") is NOT the browser's login client (UiClientId,
        // "PuckDrop-UI") for Keycloak - unlike Cognito, which only has one client for both.
        // Getting this wrong means the UI tries to log in with the wrong client entirely.
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Keycloak:ServerUrl"] = "https://keycloak.example.com",
            ["Keycloak:Realm"] = "PuckDrop",
            ["Keycloak:ClientId"] = "PuckDrop-API",
            ["Keycloak:UiClientId"] = "PuckDrop-UI"
        });

        services.AddApis(config);
        var options = services.BuildServiceProvider().GetRequiredService<AuthDiscoveryOptions>();

        Assert.Equal("https://keycloak.example.com/realms/PuckDrop", options.Authority);
        Assert.Equal("PuckDrop-UI", options.ClientId);
    }

    [Fact]
    public void AddApis_CognitoConfiguredWithPlaceholder_FallsThroughAndThrows()
    {
        // "PLACEHOLDER" is how an unfilled-in local/CI config is distinguished from a real one -
        // confirm it's treated the same as "not configured", not as a valid (broken) config.
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cognito:UserPoolId"] = "PLACEHOLDER",
            ["Cognito:ClientId"] = "PLACEHOLDER",
            ["Cognito:Region"] = "eu-west-1"
        });

        Assert.Throws<InvalidOperationException>(() => services.AddApis(config));
    }
}
