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
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonDynamoDB>();

        // Repositories
        services.AddScoped<ISeasonRepository, DynamoDbSeasonRepository>();
        services.AddScoped<IPollRepository, DynamoDbPollRepository>();
        services.AddScoped<IUserAnswerRepository, DynamoDbUserAnswerRepository>();
        services.AddScoped<ILeaderboardRepository, DynamoDbLeaderboardRepository>();

        return services;
    }
}
