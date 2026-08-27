using System;
using Microsoft.Extensions.DependencyInjection;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Xunit;

namespace Uninstaller.App.Tests.Navigation;

public class NavigationServiceTests
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationServiceTests()
    {
        var services = new ServiceCollection();
        
        var errorBoundary = new ErrorBoundaryService();
        services.AddSingleton<IErrorBoundaryService>(errorBoundary);
        
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ApplicationsViewModel>(sp => null!); // Mocked out or stubbed later if needed for full tests
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<RecoveryViewModel>();
        services.AddTransient<SettingsViewModel>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void NavigateTo_Dashboard_SetsCurrentViewModel()
    {
        var nav = new NavigationService(_serviceProvider);
        nav.NavigateTo<DashboardViewModel>();
        Assert.IsType<DashboardViewModel>(nav.CurrentViewModel);
    }

    [Fact]
    public void NavigateTo_History_SetsCurrentViewModel()
    {
        var nav = new NavigationService(_serviceProvider);
        nav.NavigateTo<HistoryViewModel>();
        Assert.IsType<HistoryViewModel>(nav.CurrentViewModel);
    }
    
    [Fact]
    public void NavigateTo_Recovery_SetsCurrentViewModel()
    {
        var nav = new NavigationService(_serviceProvider);
        nav.NavigateTo<RecoveryViewModel>();
        Assert.IsType<RecoveryViewModel>(nav.CurrentViewModel);
    }
    
    [Fact]
    public void NavigateTo_Settings_SetsCurrentViewModel()
    {
        var nav = new NavigationService(_serviceProvider);
        nav.NavigateTo<SettingsViewModel>();
        Assert.IsType<SettingsViewModel>(nav.CurrentViewModel);
    }
}
