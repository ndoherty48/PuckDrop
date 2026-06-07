using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PuckDrop.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        
        return services;
    }
}