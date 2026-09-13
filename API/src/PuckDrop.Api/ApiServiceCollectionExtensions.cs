using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Filters;

namespace PuckDrop.Api;

public static class ApiServiceCollectionExtensions
{
    public const string AdminPolicy = "AdminOnly";

    public static IServiceCollection AddApis(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options =>
        {
            options.Filters.Add<DomainExceptionFilter>();
        });

        var cognitoSettings = configuration.GetSection(CognitoSettings.SectionName).Get<CognitoSettings>();
        var keycloakSettings = configuration.GetSection(KeycloakSettings.SectionName).Get<KeycloakSettings>();

        if (cognitoSettings is not null && !cognitoSettings.UserPoolId.Contains("PLACEHOLDER"))
        {
            services.AddSingleton(cognitoSettings);
            services.AddTransient<IClaimsTransformation, CognitoClaimsTransformation>();
            // Cognito has only one UserPoolClient serving both the API's own JWT-audience
            // validation and the browser's OIDC login flow, so ClientId is correct for both.
            services.AddSingleton(new AuthDiscoveryOptions(cognitoSettings.Authority, cognitoSettings.ClientId, "code"));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = cognitoSettings.Authority;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = cognitoSettings.Authority,
                        // Cognito access tokens carry no "aud" claim at all unless you configure
                        // a resource server (confirmed against AWS's own docs) - this app doesn't,
                        // so ValidateAudience=true/ValidAudience here would reject every single
                        // token, valid or not (confirmed live: an admin-group user's requests came
                        // back 403, even though API Gateway's own JWT authorizer - which already
                        // has documented Cognito-aware "check client_id when aud is absent"
                        // fallback logic - had accepted the exact same token). The client
                        // identity check happens below instead, against "client_id" - the claim
                        // Cognito does emit, matching how API Gateway's authorizer itself
                        // validates the client for this same token.
                        ValidateAudience = false,
                        ValidateLifetime = true
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            if (context.Principal?.FindFirst("client_id")?.Value != cognitoSettings.ClientId)
                                context.Fail("Token was not issued for this app client.");

                            return Task.CompletedTask;
                        }
                    };
                });
        }
        else if (keycloakSettings is not null && !keycloakSettings.Realm.Contains("PLACEHOLDER"))
        {
            services.AddSingleton(keycloakSettings);
            services.AddTransient<IClaimsTransformation, KeycloakClaimsTransformation>();
            // Keycloak's realm has two separate clients: keycloakSettings.ClientId ("PuckDrop-API")
            // is only for the API's own audience validation - the browser logs in with the
            // different UiClientId ("PuckDrop-UI"), so that's what gets served to the UI here.
            services.AddSingleton(new AuthDiscoveryOptions(
                $"{keycloakSettings.ServerUrl}/realms/{keycloakSettings.Realm}", keycloakSettings.UiClientId, "code"));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = $"{keycloakSettings.ServerUrl}/realms/{keycloakSettings.Realm}";
                    options.Audience = keycloakSettings.ClientId;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = $"{keycloakSettings.ServerUrl}/realms/{keycloakSettings.Realm}",
                        ValidateAudience = true,
                        ValidAudience = keycloakSettings.ClientId,
                        ValidateLifetime = true
                    };
                });
        }
        else
        {
            // No real auth configured, and no dev-auth bypass to fall back to (that was
            // DevAuthenticationHandler, removed deliberately - a shipped auto-admin-bypass isn't
            // worth carrying forward). Fail fast and clearly rather than come up with broken auth.
            throw new InvalidOperationException(
                "No identity provider configured: either the 'Cognito' or 'Keycloak' configuration section must be bound and non-placeholder.");
        }

        // One provider-agnostic admin policy. Whichever branch above ran has already normalized
        // that provider's own admin signal (Cognito's "cognito:groups", Keycloak's
        // "realm_access.roles", or the dev handler's claim) into a standard ClaimTypes.Role
        // "admin" claim - via IClaimsTransformation, or directly for the dev handler - so this
        // policy (and every controller using it) never needs to know which provider is active.
        // Switching Cognito <-> Keycloak is then purely a config change, not a code change.
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("admin"));

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        services.AddLambdaServiceDefaults();
        return services;
    }

    public static IApplicationBuilder UseApis(this IApplicationBuilder app)
    {
        app.UseHttpsRedirection();
        app.UseCors();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapDefaultEndpoints();
            endpoints.MapControllers();
        });
        return app;
    }
}
