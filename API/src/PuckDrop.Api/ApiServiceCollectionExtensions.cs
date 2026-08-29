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

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = cognitoSettings.Authority;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = cognitoSettings.Authority,
                        ValidateAudience = true,
                        ValidAudience = cognitoSettings.ClientId,
                        ValidateLifetime = true
                    };
                });
        }
        else if (keycloakSettings is not null && !keycloakSettings.Realm.Contains("PLACEHOLDER"))
        {
            services.AddSingleton(keycloakSettings);
            services.AddTransient<IClaimsTransformation, KeycloakClaimsTransformation>();

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
        #if DEBUG
        else
        {
            // Dev mode: no real auth configured. Allow all requests as an admin dev user
            // (DevAuthenticationHandler emits the normalized admin role claim directly, since
            // there's no real provider claim shape to transform here).
            services.AddAuthentication("DevScheme")
                .AddScheme<DevAuthenticationOptions, DevAuthenticationHandler>("DevScheme", null);
        }
        #endif

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
