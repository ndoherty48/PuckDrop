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
            // One Cognito client serves both the API and the browser login.
            services.AddSingleton(new AuthDiscoveryOptions(
                cognitoSettings.Authority, cognitoSettings.ClientId, "code", UseCognitoLogout: true));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = cognitoSettings.Authority;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = cognitoSettings.Authority,
                        // Cognito access tokens have no "aud" claim, so the client is checked
                        // against "client_id" in OnTokenValidated instead.
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
            // The browser logs in with UiClientId; ClientId is only for audience validation.
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
            // Fail fast rather than start with no auth.
            throw new InvalidOperationException(
                "No identity provider configured: either the 'Cognito' or 'Keycloak' configuration section must be bound and non-placeholder.");
        }

        // The claims transformations above map each provider's admin signal to the "admin" role.
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

        // Display names for answer submissions - see UserProfileService.
        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.AddScoped<Application.Services.Abstractions.IUserProfileService, UserProfileService>();

        services.AddLambdaServiceDefaults(configuration);
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
