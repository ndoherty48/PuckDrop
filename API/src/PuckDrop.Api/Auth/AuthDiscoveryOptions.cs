namespace PuckDrop.Api.Auth;

/// <summary>
/// The active provider's OIDC settings for the Blazor app, served unauthenticated by
/// <see cref="PuckDrop.Api.Controllers.AuthConfigController"/> because a static WASM bundle can't
/// receive CDK-provisioned values at build time.
/// </summary>
/// <param name="Authority">The OIDC issuer/authority URL.</param>
/// <param name="ClientId">
/// The client the browser logs in with. Cognito uses one client for everything; Keycloak's is
/// <see cref="KeycloakSettings.UiClientId"/>, not the API's <see cref="KeycloakSettings.ClientId"/>.
/// </param>
/// <param name="ResponseType">The OAuth response type (always "code").</param>
/// <param name="UseCognitoLogout">
/// True for Cognito, whose <c>/logout</c> needs <c>client_id</c> and <c>logout_uri</c> instead of
/// the standard OIDC parameters, or it redirects to <c>/login</c>.
/// </param>
public record AuthDiscoveryOptions(string Authority, string ClientId, string ResponseType, bool UseCognitoLogout = false);
