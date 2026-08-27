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
            .ConfigureServices((context, services) =>
            {
                services.AddCore();
                services.AddInfrastructure();
                services.AddWindows();
                services.AddSingleton<Services.ObservableItemExecutionTracker>();
                services.AddSingleton<Services.IObservableItemExecutionTracker>(sp => sp.GetRequiredService<Services.ObservableItemExecutionTracker>());
                services.AddSingleton<Uninstaller.Core.Abstractions.IItemExecutionTracker>(sp => sp.GetRequiredService<Services.ObservableItemExecutionTracker>());
                services.AddSingleton<Services.IErrorBoundaryService, Services.ErrorBoundaryService>();
                services.AddSingleton<Services.INavigationService, Services.NavigationService>();
                
                // ViewModels
                services.AddTransient<ViewModels.MainViewModel>();
                services.AddTransient<ViewModels.DashboardViewModel>();
                services.AddTransient<ViewModels.ApplicationsViewModel>();
                services.AddTransient<ViewModels.ApplicationDetailsViewModel>();
                services.AddTransient<ViewModels.CleanupPlanViewModel>();
                services.AddTransient<ViewModels.CleanupExecutionViewModel>();
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

