using System;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Uninstaller.Core;
using Uninstaller.Infrastructure;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Windows;

namespace Uninstaller.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider((context, options) =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureServices((context, services) =>
            {
                services.AddCore();
                services.AddInfrastructure();
                services.AddWindows();
                services.AddSingleton<Services.ObservableItemExecutionTracker>();
                services.AddSingleton<Services.IObservableItemExecutionTracker>(sp => sp.GetRequiredService<Services.ObservableItemExecutionTracker>());
                services.AddSingleton<Uninstaller.Core.Abstractions.IItemExecutionTracker>(sp => sp.GetRequiredService<Services.ObservableItemExecutionTracker>());
                
                services.AddSingleton<Services.ObservableRecoveryItemExecutionTracker>();
                services.AddSingleton<Services.IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<Services.ObservableRecoveryItemExecutionTracker>());
                services.AddSingleton<Uninstaller.Core.Abstractions.IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<Services.ObservableRecoveryItemExecutionTracker>());

                services.AddSingleton<Services.IErrorBoundaryService, Services.ErrorBoundaryService>();
                services.AddSingleton<Services.INavigationService, Services.NavigationService>();
                services.AddSingleton<Services.ICleanupViewModelFactory, Services.CleanupViewModelFactory>();
                services.AddSingleton<Services.IHistoryViewModelFactory, Services.HistoryViewModelFactory>();
                
                // ViewModels resolved directly via DI
                services.AddTransient<ViewModels.MainViewModel>();
                services.AddTransient<ViewModels.DashboardViewModel>();
                services.AddTransient<ViewModels.ApplicationsViewModel>();
                services.AddTransient<ViewModels.ApplicationDetailsViewModel>();
                services.AddTransient<ViewModels.RecoveryViewModel>();
                services.AddTransient<ViewModels.HistoryViewModel>();
                services.AddTransient<ViewModels.SettingsViewModel>();
                services.AddTransient<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await _host.StartAsync();

            using (var scope = _host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();

                var recoveryService = scope.ServiceProvider.GetRequiredService<Uninstaller.Core.Abstractions.IStartupRecoveryService>();
                bool interrupted = await recoveryService.ReconcileIncompleteTransactionsAsync();
                if (interrupted)
                {
                    MessageBox.Show("An interrupted operation was detected during the last session. The system has automatically reconciled the state. Please review the History or Recovery tabs for details.", "Recovery Reconciled", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start correctly");
            MessageBox.Show($"Application failed to start: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
