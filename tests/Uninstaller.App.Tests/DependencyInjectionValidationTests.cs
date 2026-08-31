using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Uninstaller.Core;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Infrastructure;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Windows;
using Xunit;

namespace Uninstaller.App.Tests;

public class DependencyInjectionValidationTests
{
    private ServiceProvider CreateProductionServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Match production registrations from App.xaml.cs
        services.AddCore();
        services.AddInfrastructure();
        services.AddWindows();
        
        services.AddSingleton<global::Uninstaller.App.Services.ObservableItemExecutionTracker>();
        services.AddSingleton<global::Uninstaller.App.Services.IObservableItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableItemExecutionTracker>());
        services.AddSingleton<Core.Abstractions.IItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableItemExecutionTracker>());
        
        services.AddSingleton<global::Uninstaller.App.Services.ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<global::Uninstaller.App.Services.IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<Core.Abstractions.IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableRecoveryItemExecutionTracker>());

        services.AddSingleton<global::Uninstaller.App.Services.IErrorBoundaryService, global::Uninstaller.App.Services.ErrorBoundaryService>();
        services.AddSingleton<global::Uninstaller.App.Services.INavigationService, global::Uninstaller.App.Services.NavigationService>();
        
        // ViewModels resolved directly via DI
        services.AddTransient<global::Uninstaller.App.ViewModels.MainViewModel>();
        services.AddTransient<global::Uninstaller.App.ViewModels.DashboardViewModel>();
        services.AddTransient<global::Uninstaller.App.ViewModels.ApplicationsViewModel>();
        services.AddTransient<global::Uninstaller.App.ViewModels.ApplicationDetailsViewModel>();
        services.AddTransient<global::Uninstaller.App.ViewModels.RecoveryViewModel>();
        services.AddTransient<global::Uninstaller.App.ViewModels.HistoryViewModel>();
        services.AddTransient<global::Uninstaller.App.ViewModels.SettingsViewModel>();
        services.AddTransient<global::Uninstaller.App.MainWindow>();

        // Build with strict validation
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public void ProductionContainer_ValidatesOnBuildAndScopes()
    {
        var provider = CreateProductionServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void MainViewModel_NavigateToApplications_SucceedsUnderValidateScopes()
    {
        // This test directly verifies that clicking 'Applications' from MainViewModel does not throw InvalidOperationException
        var provider = CreateProductionServiceProvider();
        
        var navService = provider.GetRequiredService<global::Uninstaller.App.Services.INavigationService>();
        var mainVm = new global::Uninstaller.App.ViewModels.MainViewModel(navService);
        
        // Initial state is Dashboard
        Assert.IsType<global::Uninstaller.App.ViewModels.DashboardViewModel>(mainVm.CurrentViewModel);

        // Navigate to Applications
        mainVm.NavigateToApplicationsCommand.Execute(null);

        // Verify successful resolution and active ViewModel
        Assert.NotNull(mainVm.CurrentViewModel);
        Assert.IsType<global::Uninstaller.App.ViewModels.ApplicationsViewModel>(mainVm.CurrentViewModel);
    }

    [Fact]
    public void NavigationService_FullLifecycle_MaintainsAndDisposesScopesCleanly()
    {
        var provider = CreateProductionServiceProvider();
        var navService = provider.GetRequiredService<global::Uninstaller.App.Services.INavigationService>();

        // 1. Navigate to Applications
        var appsVm = navService.NavigateTo<global::Uninstaller.App.ViewModels.ApplicationsViewModel>();
        Assert.NotNull(appsVm);
        Assert.Same(appsVm, navService.CurrentViewModel);

        // 2. Navigate to ApplicationDetails
        var detailsVm = navService.NavigateTo<global::Uninstaller.App.ViewModels.ApplicationDetailsViewModel>();
        Assert.NotNull(detailsVm);
        Assert.Same(detailsVm, navService.CurrentViewModel);

        // 3. Navigate to History
        var historyVm = navService.NavigateTo<global::Uninstaller.App.ViewModels.HistoryViewModel>();
        Assert.NotNull(historyVm);
        Assert.Same(historyVm, navService.CurrentViewModel);

        // 4. Navigate to Recovery
        var recoveryVm = navService.NavigateTo<global::Uninstaller.App.ViewModels.RecoveryViewModel>();
        Assert.NotNull(recoveryVm);
        Assert.Same(recoveryVm, navService.CurrentViewModel);

        // 5. Navigate to Settings
        var settingsVm = navService.NavigateTo<global::Uninstaller.App.ViewModels.SettingsViewModel>();
        Assert.NotNull(settingsVm);
        Assert.Same(settingsVm, navService.CurrentViewModel);

        // 6. Navigate to Dashboard
        var dashboardVm = navService.NavigateTo<global::Uninstaller.App.ViewModels.DashboardViewModel>();
        Assert.NotNull(dashboardVm);
        Assert.Same(dashboardVm, navService.CurrentViewModel);
    }

    [Fact]
    public void RapidNavigation_DoesNotThrowOrLeak()
    {
        var provider = CreateProductionServiceProvider();
        var navService = provider.GetRequiredService<global::Uninstaller.App.Services.INavigationService>();

        for (int i = 0; i < 50; i++)
        {
            navService.NavigateTo<global::Uninstaller.App.ViewModels.ApplicationsViewModel>();
            navService.NavigateTo<global::Uninstaller.App.ViewModels.HistoryViewModel>();
            navService.NavigateTo<global::Uninstaller.App.ViewModels.DashboardViewModel>();
        }

        Assert.IsType<global::Uninstaller.App.ViewModels.DashboardViewModel>(navService.CurrentViewModel);
    }

    [Fact]
    public void Resolve_AllMajorServices_InScopedContext_Succeeds()
    {
        var provider = CreateProductionServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var uninstallService = sp.GetRequiredService<IUninstallService>();
        Assert.NotNull(uninstallService);
        Assert.IsType<UninstallService>(uninstallService);

        var residualAnalysisService = sp.GetRequiredService<IResidualAnalysisService>();
        Assert.NotNull(residualAnalysisService);
        Assert.IsType<ResidualAnalysisService>(residualAnalysisService);

        var planGenerator = sp.GetRequiredService<ICleanupPlanGenerator>();
        Assert.NotNull(planGenerator);
        Assert.IsType<CleanupPlanGenerator>(planGenerator);

        var evidenceEngine = sp.GetRequiredService<IEvidenceEngine>();
        Assert.NotNull(evidenceEngine);
        Assert.IsType<EvidenceEngine>(evidenceEngine);

        var discoveryService = sp.GetRequiredService<IDiscoveryService>();
        Assert.NotNull(discoveryService);
        Assert.IsType<DiscoveryService>(discoveryService);
    }

    [Fact]
    public void Verify_NoSingletonCapturesScopedService()
    {
        var services = new ServiceCollection();
        services.AddCore();
        services.AddInfrastructure();
        services.AddWindows();
        services.AddSingleton<global::Uninstaller.App.Services.ObservableItemExecutionTracker>();
        services.AddSingleton<global::Uninstaller.App.Services.IObservableItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableItemExecutionTracker>());
        services.AddSingleton<Core.Abstractions.IItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableItemExecutionTracker>());
        services.AddSingleton<global::Uninstaller.App.Services.ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<global::Uninstaller.App.Services.IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<Core.Abstractions.IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<global::Uninstaller.App.Services.ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<global::Uninstaller.App.Services.IErrorBoundaryService, global::Uninstaller.App.Services.ErrorBoundaryService>();
        services.AddSingleton<global::Uninstaller.App.Services.INavigationService, global::Uninstaller.App.Services.NavigationService>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Resolve all singletons from root to verify none trigger scope capture exceptions
        var normalizer = provider.GetRequiredService<IApplicationNormalizer>();
        Assert.NotNull(normalizer);

        var deduplicator = provider.GetRequiredService<IApplicationDeduplicator>();
        Assert.NotNull(deduplicator);

        var parser = provider.GetRequiredService<ICommandParser>();
        Assert.NotNull(parser);

        var pathResolver = provider.GetRequiredService<ICanonicalPathResolver>();
        Assert.NotNull(pathResolver);

        var fileExecutor = provider.GetRequiredService<IFileCleanupExecutor>();
        Assert.NotNull(fileExecutor);

        var registryExecutor = provider.GetRequiredService<IRegistryCleanupExecutor>();
        Assert.NotNull(registryExecutor);

        var shortcutExecutor = provider.GetRequiredService<IShortcutCleanupExecutor>();
        Assert.NotNull(shortcutExecutor);

        var backupStorage = provider.GetRequiredService<IBackupStorage>();
        Assert.NotNull(backupStorage);

        var nav = provider.GetRequiredService<global::Uninstaller.App.Services.INavigationService>();
        Assert.NotNull(nav);

        var errorBoundary = provider.GetRequiredService<global::Uninstaller.App.Services.IErrorBoundaryService>();
        Assert.NotNull(errorBoundary);
    }
}
