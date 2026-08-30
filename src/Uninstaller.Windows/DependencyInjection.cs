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
        services.AddTransient<System.IO.Abstractions.IFileSystem, System.IO.Abstractions.FileSystem>();
        services.AddTransient<Uninstaller.Windows.Filesystem.IShortcutProvider, Uninstaller.Windows.Filesystem.ShortcutProvider>();
        services.AddTransient<IShortcutService, Uninstaller.Windows.FileSystem.WindowsShortcutService>();
        services.AddTransient<Uninstaller.Windows.Registry.IRegistryProvider, Uninstaller.Windows.Registry.RegistryProvider>();
        services.AddScoped<IResidualScanner, Uninstaller.Windows.Registry.WindowsRegistryScanner>();
        services.AddScoped<IResidualScanner, Uninstaller.Windows.Filesystem.WindowsFilesystemScanner>();
        services.AddScoped<IResidualScanner, Uninstaller.Windows.Filesystem.WindowsShortcutScanner>();
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
