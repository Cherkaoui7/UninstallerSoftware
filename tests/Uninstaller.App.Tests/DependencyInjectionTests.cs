using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Uninstaller.Core;
using Uninstaller.Infrastructure;
using Uninstaller.Windows;
using Uninstaller.App;
using Uninstaller.App.ViewModels;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Xunit;

namespace Uninstaller.App.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AppDependencyInjection_CanResolveApplicationDetailsViewModel()
    {
        // Arrange
        var services = new ServiceCollection();
        
        services.AddCore();
        services.AddInfrastructure();
        services.AddWindows();
        
        services.AddSingleton<ObservableItemExecutionTracker>();
        services.AddSingleton<IObservableItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        services.AddSingleton<IItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        
        services.AddSingleton<ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());

        services.AddSingleton<IErrorBoundaryService, ErrorBoundaryService>();
        services.AddSingleton<INavigationService, NavigationService>();
        
        services.AddTransient<ApplicationDetailsViewModel>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Act & Assert
        // If any dependency (e.g., IEvidenceEngine) is missing, this will throw an InvalidOperationException
        using var scope = provider.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<ApplicationDetailsViewModel>();
        
        Assert.NotNull(viewModel);
    }

    [Fact]
    public void AppDependencyInjection_CanResolveCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();
        
        services.AddCore();
        services.AddInfrastructure();
        services.AddWindows();
        
        services.AddSingleton<ObservableItemExecutionTracker>();
        services.AddSingleton<IObservableItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        services.AddSingleton<IItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        
        services.AddSingleton<ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Act & Assert
        using var scope = provider.CreateScope();
        
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDiscoveryService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IResidualAnalysisService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEvidenceEngine>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICleanupPlanGenerator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICleanupPreflightValidator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBackupService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICleanupTransactionEngine>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRecoveryTransactionEngine>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITransactionJournal>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStartupRecoveryService>());
    }
}
