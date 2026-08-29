namespace PuckDrop.Api.Auth;

/// <summary>
/// The OIDC config the Blazor WASM app needs to log in against whichever provider is active
/// (Cognito or Keycloak), normalized into one shape - registered by whichever branch of
/// <see cref="ApiServiceCollectionExtensions.AddApis"/> resolved, and served unauthenticated via
/// <see cref="PuckDrop.Api.Controllers.AuthConfigController"/> so the UI can fetch it at boot instead of
/// having it baked into <c>wwwroot/appsettings.json</c> at build time (there's no way to inject
/// CDK-provisioned values into a static WASM bundle the way the Lambda's env vars are injected
/// at publish time).
/// </summary>
/// <param name="Authority">The OIDC issuer/authority URL.</param>
/// <param name="ClientId">
/// The browser-facing OIDC client ID. For Cognito this is the same client used for the API's own
/// JWT-audience validation (<see cref="CognitoSettings.ClientId"/>) - Cognito only has one
/// UserPoolClient serving both purposes. For Keycloak it is NOT the same as
/// <see cref="KeycloakSettings.ClientId"/> (that one is the API's own audience-validation
/// client, "PuckDrop-API") - it's <see cref="KeycloakSettings.UiClientId"/> ("PuckDrop-UI"), the
/// separate public client the browser actually logs in with.
/// </param>
/// <param name="ResponseType">The OAuth response type (always "code" - authorization code flow).</param>
public record AuthDiscoveryOptions(string Authority, string ClientId, string ResponseType);
