using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PuckDrop.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        
        return services;
    }
}