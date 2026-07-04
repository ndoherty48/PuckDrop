using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Application.Repositories;
using PuckDrop.Infrastructure.DynamoDb.Repositories;

namespace PuckDrop.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // IAmazonDynamoDB is registered by Aspire's AWS hosting integration
        // For non-Aspire environments, register it manually:
        if (services.All(s => s.ServiceType != typeof(IAmazonDynamoDB)))
        {
            services.AddSingleton<IAmazonDynamoDB>(sp =>
            {
                var config = new AmazonDynamoDBConfig();
                var serviceUrl = configuration["DynamoDb:ServiceUrl"];
                if (!string.IsNullOrEmpty(serviceUrl))
                    config.ServiceURL = serviceUrl;

                return new AmazonDynamoDBClient(config);
            });
        }

        // Repositories
        services.AddScoped<ISeasonRepository, DynamoDbSeasonRepository>();
        services.AddScoped<IPollRepository, DynamoDbPollRepository>();
        services.AddScoped<IUserAnswerRepository, DynamoDbUserAnswerRepository>();
        services.AddScoped<ILeaderboardRepository, DynamoDbLeaderboardRepository>();

        return services;
    }
}
