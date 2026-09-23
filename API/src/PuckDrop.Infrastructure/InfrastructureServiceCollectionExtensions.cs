using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services.Abstractions;
using PuckDrop.Infrastructure.DynamoDb.Repositories;
using PuckDrop.Infrastructure.Fixtures;

namespace PuckDrop.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonDynamoDB>();

        // Repositories
        services.AddScoped<ISeasonRepository, DynamoDbSeasonRepository>();
        services.AddScoped<IPollRepository, DynamoDbPollRepository>();
        services.AddScoped<IUserAnswerRepository, DynamoDbUserAnswerRepository>();
        services.AddScoped<ILeaderboardRepository, DynamoDbLeaderboardRepository>();

        // External feeds
        services.AddHttpClient<IIcsFixtureFeedFetcher, IcsFixtureFeedFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            // Some hosts (e.g. GitHub-backed static content) 403 a request with no User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PuckDrop/1.0 (+https://github.com/ndoherty48/PuckDrop)");
        });

        return services;
    }
}
