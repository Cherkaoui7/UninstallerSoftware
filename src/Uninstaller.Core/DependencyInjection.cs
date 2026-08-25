using Microsoft.Extensions.DependencyInjection;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;

namespace Uninstaller.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddScoped<IApplicationNormalizer, ApplicationNormalizer>();
        services.AddScoped<IApplicationDeduplicator, ApplicationDeduplicator>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        return services;
    }
}
