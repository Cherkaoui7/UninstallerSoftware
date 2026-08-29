using Microsoft.Extensions.DependencyInjection;
using Uninstaller.Core.Abstractions;
using Uninstaller.Windows.FileSystem;
using Uninstaller.Windows.Cleanup;
using Uninstaller.Windows.Processes;
using Uninstaller.Windows.Registry;
using Uninstaller.Windows.Services;
using Uninstaller.Windows.Tasks;
using Uninstaller.Windows.Backup;

namespace Uninstaller.Windows;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
        services.AddSingleton<ICanonicalPathResolver, Uninstaller.Windows.Filesystem.WindowsCanonicalPathResolver>();
        services.AddSingleton<IFileCleanupExecutor, WindowsFileCleanupExecutor>();
        services.AddSingleton<IRegistryCleanupExecutor, WindowsRegistryCleanupExecutor>();
        services.AddSingleton<IShortcutCleanupExecutor, WindowsShortcutCleanupExecutor>();
        services.AddSingleton<IBackupStorage, WindowsBackupStorage>();
        services.AddTransient<IFileBackupProvider, WindowsFileBackupProvider>();
        services.AddTransient<IRegistryBackupProvider, WindowsRegistryBackupProvider>();
        return services;
    }
}
