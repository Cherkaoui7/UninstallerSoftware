using Microsoft.Extensions.DependencyInjection;
using Uninstaller.Core.Abstractions;
using Uninstaller.Windows.FileSystem;
using Uninstaller.Windows.Processes;
using Uninstaller.Windows.Registry;
using Uninstaller.Windows.Services;
using Uninstaller.Windows.Tasks;

namespace Uninstaller.Windows;

public static class DependencyInjection
{
    public static IServiceCollection AddWindows(this IServiceCollection services)
    {
        services.AddTransient<IRegistryService, WindowsRegistryService>();
        services.AddTransient<IFileSystemService, WindowsFileSystemService>();
        services.AddTransient<IProcessService, WindowsProcessService>();
        services.AddTransient<IServiceManager, WindowsServiceManager>();
        services.AddTransient<ITaskScheduler, WindowsTaskScheduler>();
        return services;
    }
}
