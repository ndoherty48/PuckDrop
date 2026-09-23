using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Application.Services;

namespace PuckDrop.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SeasonService>();
        services.AddScoped<PollService>();
        services.AddScoped<AnswerService>();
        services.AddScoped<ScoringService>();
        services.AddScoped<LeaderboardService>();
        services.AddScoped<ResultsService>();
        services.AddScoped<PollVoidService>();
        services.AddScoped<PointAdjustmentService>();
        services.AddScoped<FixtureImportService>();

        return services;
    }
}
