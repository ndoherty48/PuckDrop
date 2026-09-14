using PuckDrop.Api.Auth;
using PuckDrop.Api.Contracts;

namespace PuckDrop.Api.Mappings;

public static class AuthConfigMappingExtensions
{
    public static AuthConfigResponse ToResponse(this AuthDiscoveryOptions options) => new(
        options.Authority, options.ClientId, options.ResponseType, options.UseCognitoLogout);
}
