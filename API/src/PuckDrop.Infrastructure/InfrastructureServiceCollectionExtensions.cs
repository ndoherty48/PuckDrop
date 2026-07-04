using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PuckDrop.Domain.Repositories;
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
        services.AddSingleton<ISeasonRepository, DynamoDbSeasonRepository>();
        services.AddSingleton<IPollRepository, DynamoDbPollRepository>();
        services.AddSingleton<IUserAnswerRepository, DynamoDbUserAnswerRepository>();
        services.AddSingleton<ILeaderboardRepository, DynamoDbLeaderboardRepository>();

        return services;
    }
}
