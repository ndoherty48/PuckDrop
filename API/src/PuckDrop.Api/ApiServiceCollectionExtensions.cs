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

            services.AddAuthorizationBuilder()
                .AddPolicy(AdminPolicy, policy =>
                    policy.RequireAssertion(context =>
                        context.User.HasClaim(c =>
                            c.Type == "cognito:groups" && c.Value == cognitoSettings.AdminGroupName)));
        }
        else if (keycloakSettings is not null && !keycloakSettings.Realm.Contains("PLACEHOLDER"))
        {
            services.AddSingleton(keycloakSettings);

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

            services.AddAuthorizationBuilder()
                .AddPolicy(AdminPolicy, policy =>
                    policy.RequireAssertion(context =>
                        context.User.HasClaim(c =>
                            c.Type == "realm_access" && 
                            c.Value.Contains(keycloakSettings.AdminRoleName))));
        }
        #if DEBUG
        else
        {
            // Dev mode: no real auth configured. Allow all requests.
            services.AddAuthentication("DevScheme")
                .AddScheme<DevAuthenticationOptions, DevAuthenticationHandler>("DevScheme", null);

            services.AddAuthorizationBuilder()
                .AddPolicy(AdminPolicy, policy => policy.RequireAssertion(_ => true));
        }
        #endif

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
