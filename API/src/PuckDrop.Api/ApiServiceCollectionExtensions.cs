using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PuckDrop.Api.Auth;
using PuckDrop.Api.Filters;
using PuckDrop.Application.Services.Abstractions;

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

        services.AddHttpContextAccessor();
        services.AddScoped<IUserProfileService, CognitoUserProfileService>();

        var cognitoSettings = configuration.GetSection(CognitoSettings.SectionName).Get<CognitoSettings>();

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
        else
        {
            // Dev mode: no real auth configured. Allow all requests.
            services.AddAuthentication("DevScheme")
                .AddScheme<DevAuthenticationOptions, DevAuthenticationHandler>("DevScheme", null);

            services.AddAuthorizationBuilder()
                .AddPolicy(AdminPolicy, policy => policy.RequireAssertion(_ => true));
        }

        services.AddLambdaServiceDefaults();
        return services;
    }

    public static IApplicationBuilder UseApis(this IApplicationBuilder app)
    {
        app.UseHttpsRedirection();
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
