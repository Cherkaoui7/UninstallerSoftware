using Microsoft.Extensions.DependencyInjection;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;

namespace Uninstaller.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddSingleton<IApplicationNormalizer, ApplicationNormalizer>();
        services.AddSingleton<IApplicationDeduplicator, ApplicationDeduplicator>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddScoped<IUninstallService, UninstallService>();
        services.AddScoped<IBackupService, BackupService>();

        return services;
    }
}
