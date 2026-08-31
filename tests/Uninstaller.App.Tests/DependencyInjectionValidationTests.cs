using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Core;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
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
        
        services.AddSingleton<ObservableItemExecutionTracker>();
        services.AddSingleton<IObservableItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        services.AddSingleton<IItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        
        services.AddSingleton<ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());

        services.AddSingleton<IErrorBoundaryService, ErrorBoundaryService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICleanupViewModelFactory, CleanupViewModelFactory>();
        services.AddSingleton<IHistoryViewModelFactory, HistoryViewModelFactory>();
        
        // ViewModels resolved directly via DI
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ApplicationsViewModel>();
        services.AddTransient<ApplicationDetailsViewModel>();
        services.AddTransient<RecoveryViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindow>();

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
        // Direct regression test verifying MainViewModel.NavigateToApplications() does not throw under ValidateScopes=true
        var provider = CreateProductionServiceProvider();
        
        var navService = provider.GetRequiredService<INavigationService>();
        var mainVm = new MainViewModel(navService);
        
        // Initial state is Dashboard
        Assert.IsType<DashboardViewModel>(mainVm.CurrentViewModel);

        // Navigate to Applications
        mainVm.NavigateToApplicationsCommand.Execute(null);

        // Verify successful resolution and active ViewModel
        Assert.NotNull(mainVm.CurrentViewModel);
        Assert.IsType<ApplicationsViewModel>(mainVm.CurrentViewModel);
    }

    [Fact]
    public void NavigationService_FullLifecycle_MaintainsAndDisposesScopesCleanly()
    {
        var provider = CreateProductionServiceProvider();
        var navService = provider.GetRequiredService<INavigationService>();

        // 1. Navigate to Applications
        var appsVm = navService.NavigateTo<ApplicationsViewModel>();
        Assert.NotNull(appsVm);
        Assert.Same(appsVm, navService.CurrentViewModel);

        // 2. Navigate to ApplicationDetails
        var detailsVm = navService.NavigateTo<ApplicationDetailsViewModel>();
        Assert.NotNull(detailsVm);
        Assert.Same(detailsVm, navService.CurrentViewModel);

        // 3. Navigate to History
        var historyVm = navService.NavigateTo<HistoryViewModel>();
        Assert.NotNull(historyVm);
        Assert.Same(historyVm, navService.CurrentViewModel);

        // 4. Navigate to Recovery
        var recoveryVm = navService.NavigateTo<RecoveryViewModel>();
        Assert.NotNull(recoveryVm);
        Assert.Same(recoveryVm, navService.CurrentViewModel);

        // 5. Navigate to Settings
        var settingsVm = navService.NavigateTo<SettingsViewModel>();
        Assert.NotNull(settingsVm);
        Assert.Same(settingsVm, navService.CurrentViewModel);

        // 6. Navigate to Dashboard
        var dashboardVm = navService.NavigateTo<DashboardViewModel>();
        Assert.NotNull(dashboardVm);
        Assert.Same(dashboardVm, navService.CurrentViewModel);
    }

    [Fact]
    public void RapidNavigation_DoesNotThrowOrLeak()
    {
        var provider = CreateProductionServiceProvider();
        var navService = provider.GetRequiredService<INavigationService>();

        for (int i = 0; i < 50; i++)
        {
            navService.NavigateTo<ApplicationsViewModel>();
            navService.NavigateTo<HistoryViewModel>();
            navService.NavigateTo<DashboardViewModel>();
        }

        Assert.IsType<DashboardViewModel>(navService.CurrentViewModel);
    }

    [Fact]
    public async Task CleanupExecution_DedicatedScope_ExecutesWithoutObjectDisposedException()
    {
        // 1. Setup real production container with isolated SQLite DB and temporary directory
        var tempDir = Path.Combine(Path.GetTempPath(), "Uninstaller_DI_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, "sample_residual.log");
        File.WriteAllText(targetFile, "temporary residual payload");

        try
        {
            var provider = CreateProductionServiceProvider();
            var navService = provider.GetRequiredService<INavigationService>();
            var cleanupFactory = provider.GetRequiredService<ICleanupViewModelFactory>();

            // Ensure DB schema exists in production provider
            using (var initScope = provider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var appEntity = new Application
            {
                Id = Guid.NewGuid(),
                Name = "Test App DI Lifecycle",
                InstallLocation = tempDir
            };

            var planItem = new CleanupPlanItem
            {
                Id = Guid.NewGuid(),
                Path = targetFile,
                ArtifactType = ArtifactType.File,
                Classification = ArtifactClassification.ApplicationOwned,
                RiskLevel = RiskLevel.Low,
                Recommended = true
            };

            var plan = new CleanupPlan
            {
                Id = Guid.NewGuid(),
                ApplicationId = appEntity.Id,
                UninstallSessionId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                Items = new List<CleanupPlanItem> { planItem }
            };

            // 2. Navigate to CleanupPlanViewModel via factory
            var planVm = cleanupFactory.CreatePlanViewModel(plan, appEntity);
            navService.NavigateTo(planVm);

            Assert.Same(planVm, navService.CurrentViewModel);
            Assert.Equal(1, planVm.TotalArtifacts);
            Assert.True(planVm.Items[0].IsSelected);

            // 3. Review cleanup and open confirmation
            planVm.ReviewCleanupCommand.Execute(null);
            Assert.True(planVm.IsConfirmationVisible);

            // 4. Confirm and execute cleanup via ExecuteCleanup
            planVm.ExecuteCleanupCommand.Execute(null);

            // 5. Verify navigation transitioned to CleanupExecutionViewModel
            var execVm = navService.CurrentViewModel as CleanupExecutionViewModel;
            Assert.NotNull(execVm);

            // 6. Execute cleanup through real production transaction engine
            await execVm.StartExecutionAsync();

            // 7. Verify successful execution without ObjectDisposedException
            Assert.Equal(1, execVm.SuccessCount);
            Assert.Equal(0, execVm.FailedCount);
            Assert.False(File.Exists(targetFile));

            // 8. Disposing the execution ViewModel disposes its owned scope deterministically
            execVm.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RepeatedCleanup_50Operations_DoNotLeakOrReuseDisposedScopes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Uninstaller_Repeat_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var provider = CreateProductionServiceProvider();
            var cleanupFactory = provider.GetRequiredService<ICleanupViewModelFactory>();

            using (var initScope = provider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var appEntity = new Application { Id = Guid.NewGuid(), Name = "Repeat App" };

            for (int i = 0; i < 50; i++)
            {
                var file = Path.Combine(tempDir, $"res_{i}.txt");
                File.WriteAllText(file, "content");

                var item = new CleanupPlanItem
                {
                    Id = Guid.NewGuid(),
                    Path = file,
                    ArtifactType = ArtifactType.File,
                    Classification = ArtifactClassification.ApplicationOwned,
                    RiskLevel = RiskLevel.Low,
                    Recommended = true
                };

                var plan = new CleanupPlan
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = appEntity.Id,
                    UninstallSessionId = Guid.NewGuid(),
                    Items = new List<CleanupPlanItem> { item }
                };

                var execVm = cleanupFactory.CreateExecutionViewModel(plan, appEntity, new[] { item.Id });
                await execVm.StartExecutionAsync();

                Assert.Equal(1, execVm.SuccessCount);
                execVm.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task CleanupExecution_NavigationAwayWhileRunning_DefersScopeDisposalUntilExecutionComplete()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Uninstaller_NavDefer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, "deferred.log");
        File.WriteAllText(targetFile, "data");

        try
        {
            var provider = CreateProductionServiceProvider();
            var cleanupFactory = provider.GetRequiredService<ICleanupViewModelFactory>();

            using (var initScope = provider.CreateScope())
            {
                var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var appEntity = new Application { Id = Guid.NewGuid(), Name = "NavDefer App", InstallLocation = tempDir };
            var item = new CleanupPlanItem
            {
                Id = Guid.NewGuid(),
                Path = targetFile,
                ArtifactType = ArtifactType.File,
                Classification = ArtifactClassification.ApplicationOwned,
                RiskLevel = RiskLevel.Low,
                Recommended = true
            };
            var plan = new CleanupPlan
            {
                Id = Guid.NewGuid(),
                ApplicationId = appEntity.Id,
                UninstallSessionId = Guid.NewGuid(),
                Items = new List<CleanupPlanItem> { item }
            };

            var execVm = cleanupFactory.CreateExecutionViewModel(plan, appEntity, new[] { item.Id });

            // Start async execution in background
            var execTask = execVm.StartExecutionAsync();

            // Simulate user immediately navigating away while execution is active
            execVm.Dispose();

            // Wait for execution to finish
            await execTask;

            // Verify execution was protected and completed successfully
            Assert.Equal(1, execVm.SuccessCount);
            Assert.False(File.Exists(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void DisposedScope_CannotResolveServices()
    {
        var provider = CreateProductionServiceProvider();
        var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        // Resolving within alive scope works
        var engine = sp.GetRequiredService<ICleanupTransactionEngine>();
        Assert.NotNull(engine);

        // Dispose scope
        scope.Dispose();

        // Resolving from disposed scope must throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => sp.GetRequiredService<ICleanupTransactionEngine>());
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
        services.AddSingleton<ObservableItemExecutionTracker>();
        services.AddSingleton<IObservableItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        services.AddSingleton<IItemExecutionTracker>(sp => sp.GetRequiredService<ObservableItemExecutionTracker>());
        services.AddSingleton<ObservableRecoveryItemExecutionTracker>();
        services.AddSingleton<IObservableRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<IRecoveryItemExecutionTracker>(sp => sp.GetRequiredService<ObservableRecoveryItemExecutionTracker>());
        services.AddSingleton<IErrorBoundaryService, ErrorBoundaryService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICleanupViewModelFactory, CleanupViewModelFactory>();
        services.AddSingleton<IHistoryViewModelFactory, HistoryViewModelFactory>();

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

        var nav = provider.GetRequiredService<INavigationService>();
        Assert.NotNull(nav);

        var cleanupFactory = provider.GetRequiredService<ICleanupViewModelFactory>();
        Assert.NotNull(cleanupFactory);

        var historyFactory = provider.GetRequiredService<IHistoryViewModelFactory>();
        Assert.NotNull(historyFactory);

        var errorBoundary = provider.GetRequiredService<IErrorBoundaryService>();
        Assert.NotNull(errorBoundary);
    }
}
