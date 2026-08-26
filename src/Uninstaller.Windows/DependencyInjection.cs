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
        services.AddTransient<IProcessExecutor, WindowsProcessExecutor>();
        services.AddSingleton<IBackupStorage, Uninstaller.Windows.Backups.WindowsBackupStorage>();
        services.AddTransient<IFileBackupProvider, Uninstaller.Windows.Backups.WindowsFileBackupProvider>();
        services.AddTransient<IRegistryBackupProvider, Uninstaller.Windows.Backups.WindowsRegistryBackupProvider>();
        services.AddTransient<IFileCleanupExecutor, Uninstaller.Windows.Cleanup.WindowsFileCleanupExecutor>();
        return services;
    }
}
