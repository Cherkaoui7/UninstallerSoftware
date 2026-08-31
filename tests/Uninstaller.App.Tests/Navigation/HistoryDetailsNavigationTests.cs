using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.App.Views;
using Uninstaller.Core;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Infrastructure;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Windows;
using Xunit;

namespace Uninstaller.App.Tests.Navigation;

public class HistoryDetailsNavigationTests
{
    private static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
        await Task.CompletedTask;
    }

    private ServiceProvider CreateProductionServiceProvider()
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

        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ApplicationsViewModel>();
        services.AddTransient<ApplicationDetailsViewModel>();
        services.AddTransient<RecoveryViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindow>();

        services.AddLogging(builder => builder.AddDebug());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public async Task Req01_HistoryDetails_CreatesApplicationHistoryViewModel_AndNavigates()
    {
        await RunOnStaAsync(async () =>
        {
            var provider = CreateProductionServiceProvider();
            var navService = provider.GetRequiredService<INavigationService>();

            // Navigate to History via NavigationService
            var historyVm = navService.NavigateTo<HistoryViewModel>();

            var appId = Guid.NewGuid();
            var activity = new HistoryActivity
            {
                ApplicationId = appId,
                ApplicationName = "TestApp",
                ActivityType = ActivityType.OfficialUninstall,
                SessionId = Guid.NewGuid()
            };

            // Invoke details navigation via command
            historyVm.ViewSessionDetailsCommand.Execute(activity);

            Assert.NotNull(navService.CurrentViewModel);
            Assert.IsType<ApplicationHistoryViewModel>(navService.CurrentViewModel);

            var appHistoryVm = (ApplicationHistoryViewModel)navService.CurrentViewModel;
            Assert.Equal(appId, appHistoryVm.ApplicationId);
            Assert.Equal("TestApp", appHistoryVm.ApplicationName);
        });
    }

    [Fact]
    public void Req02_MainWindow_ContainsDataTemplates_ForAll12ViewModels()
    {
        RunOnSta(() =>
        {
            var expectedMappings = new Dictionary<Type, Type>
            {
                { typeof(DashboardViewModel), typeof(DashboardView) },
                { typeof(ApplicationsViewModel), typeof(ApplicationsView) },
                { typeof(ApplicationDetailsViewModel), typeof(ApplicationDetailsView) },
                { typeof(CleanupPlanViewModel), typeof(CleanupPlanView) },
                { typeof(CleanupExecutionViewModel), typeof(CleanupExecutionView) },
                { typeof(HistoryViewModel), typeof(HistoryView) },
                { typeof(ApplicationHistoryViewModel), typeof(ApplicationHistoryView) },
                { typeof(CleanupSessionHistoryViewModel), typeof(CleanupSessionHistoryView) },
                { typeof(RecoverySessionHistoryViewModel), typeof(RecoverySessionHistoryView) },
                { typeof(RecoveryViewModel), typeof(RecoveryView) },
                { typeof(RecoverySessionViewModel), typeof(RecoverySessionView) },
                { typeof(SettingsViewModel), typeof(SettingsView) }
            };

            var provider = CreateProductionServiceProvider();
            var mainVm = provider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow(mainVm);

            foreach (var mapping in expectedMappings)
            {
                var vmType = mapping.Key;
                var expectedViewType = mapping.Value;

                var key = new DataTemplateKey(vmType);
                var resource = mainWindow.Resources[key];

                Assert.NotNull(resource);
                var dataTemplate = Assert.IsType<DataTemplate>(resource);
                var visual = dataTemplate.LoadContent();
                Assert.NotNull(visual);
                Assert.IsType(expectedViewType, visual);
            }
        });
    }

    [Fact]
    public async Task Req03_HistoryDetailsNavigation_ResolvesApplicationHistoryView_NotClrTypeName()
    {
        await RunOnStaAsync(async () =>
        {
            var provider = CreateProductionServiceProvider();
            var navService = provider.GetRequiredService<INavigationService>();
            var mainVm = provider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow(mainVm);

            // 1. Start at HistoryViewModel
            var historyVm = navService.NavigateTo<HistoryViewModel>();
            Assert.IsType<HistoryViewModel>(navService.CurrentViewModel);

            // Verify DataTemplate resolution for History
            var historyKey = new DataTemplateKey(typeof(HistoryViewModel));
            var historyTemplate = (DataTemplate)mainWindow.Resources[historyKey];
            var historyView = (HistoryView)historyTemplate.LoadContent();
            Assert.NotNull(historyView);

            // 2. Click Details on a history item
            var appId = Guid.NewGuid();
            var activity = new HistoryActivity
            {
                ApplicationId = appId,
                ApplicationName = "Telegram Desktop",
                ActivityType = ActivityType.Discovery,
                SessionId = appId
            };

            historyVm.ViewSessionDetailsCommand.Execute(activity);

            // 3. CurrentViewModel is now ApplicationHistoryViewModel
            Assert.NotNull(navService.CurrentViewModel);
            Assert.IsType<ApplicationHistoryViewModel>(navService.CurrentViewModel);
            var appHistoryVm = (ApplicationHistoryViewModel)navService.CurrentViewModel;

            // 4. Resolve the visual content via the authoritative DataTemplate in MainWindow
            var appHistoryKey = new DataTemplateKey(typeof(ApplicationHistoryViewModel));
            Assert.True(mainWindow.Resources.Contains(appHistoryKey), "MainWindow.Resources must define DataTemplate for ApplicationHistoryViewModel");

            var appHistoryTemplate = (DataTemplate)mainWindow.Resources[appHistoryKey];
            var resolvedView = appHistoryTemplate.LoadContent();

            Assert.NotNull(resolvedView);
            Assert.IsType<ApplicationHistoryView>(resolvedView);
            Assert.IsNotType<TextBlock>(resolvedView);

            // 5. Test DataContext binding
            var userControl = (UserControl)resolvedView;
            userControl.DataContext = appHistoryVm;
            Assert.Same(appHistoryVm, userControl.DataContext);
            Assert.NotNull(userControl.DataContext);
        });
    }

    [Fact]
    public async Task Req04_CleanupSessionHistoryNavigation_ResolvesCleanupSessionHistoryView()
    {
        await RunOnStaAsync(async () =>
        {
            var provider = CreateProductionServiceProvider();
            var navService = provider.GetRequiredService<INavigationService>();
            var mainVm = provider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow(mainVm);

            var historyVm = navService.NavigateTo<HistoryViewModel>();
            var sessionId = Guid.NewGuid();
            var activity = new HistoryActivity
            {
                SessionId = sessionId,
                ApplicationId = Guid.NewGuid(),
                ApplicationName = "Telegram Desktop",
                ActivityType = ActivityType.Cleanup
            };

            historyVm.ViewSessionDetailsCommand.Execute(activity);

            Assert.IsType<CleanupSessionHistoryViewModel>(navService.CurrentViewModel);

            var key = new DataTemplateKey(typeof(CleanupSessionHistoryViewModel));
            Assert.True(mainWindow.Resources.Contains(key));
            var template = (DataTemplate)mainWindow.Resources[key];
            var resolved = template.LoadContent();

            Assert.IsType<CleanupSessionHistoryView>(resolved);
        });
    }

    [Fact]
    public async Task Req05_RecoverySessionHistoryNavigation_ResolvesRecoverySessionHistoryView()
    {
        await RunOnStaAsync(async () =>
        {
            var provider = CreateProductionServiceProvider();
            var navService = provider.GetRequiredService<INavigationService>();
            var mainVm = provider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow(mainVm);

            var historyVm = navService.NavigateTo<HistoryViewModel>();
            var sessionId = Guid.NewGuid();
            var activity = new HistoryActivity
            {
                SessionId = sessionId,
                ApplicationId = Guid.NewGuid(),
                ApplicationName = "Telegram Desktop",
                ActivityType = ActivityType.Recovery
            };

            historyVm.ViewSessionDetailsCommand.Execute(activity);

            Assert.IsType<RecoverySessionHistoryViewModel>(navService.CurrentViewModel);

            var key = new DataTemplateKey(typeof(RecoverySessionHistoryViewModel));
            Assert.True(mainWindow.Resources.Contains(key));
            var template = (DataTemplate)mainWindow.Resources[key];
            var resolved = template.LoadContent();

            Assert.IsType<RecoverySessionHistoryView>(resolved);
        });
    }

    [Fact]
    public async Task Req06_SwitchingHistoryDetails_A_To_B_WorksAndPreservesScopeIsolation()
    {
        await RunOnStaAsync(async () =>
        {
            var provider = CreateProductionServiceProvider();
            var navService = provider.GetRequiredService<INavigationService>();

            var historyVm = navService.NavigateTo<HistoryViewModel>();

            // Navigate to App A details
            var appA = new HistoryActivity { ApplicationId = Guid.NewGuid(), ApplicationName = "App A", ActivityType = ActivityType.Discovery };
            historyVm.ViewSessionDetailsCommand.Execute(appA);
            var vmA = Assert.IsType<ApplicationHistoryViewModel>(navService.CurrentViewModel);
            Assert.Equal("App A", vmA.ApplicationName);

            // Go back to History
            vmA.GoBackCommand.Execute(null);
            Assert.IsType<HistoryViewModel>(navService.CurrentViewModel);

            // Navigate to App B details
            var appB = new HistoryActivity { ApplicationId = Guid.NewGuid(), ApplicationName = "App B", ActivityType = ActivityType.Discovery };
            ((HistoryViewModel)navService.CurrentViewModel).ViewSessionDetailsCommand.Execute(appB);
            var vmB = Assert.IsType<ApplicationHistoryViewModel>(navService.CurrentViewModel);
            Assert.Equal("App B", vmB.ApplicationName);

            // Disposing vmA does not affect vmB
            vmA.Dispose();
            await vmB.InitializeAsync();
            Assert.NotNull(vmB.TimelineEvents);
        });
    }
}
