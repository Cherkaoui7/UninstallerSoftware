using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Uninstaller.App.ViewModels;
using Uninstaller.Core.Abstractions;

namespace Uninstaller.App.Services;

public class HistoryViewModelFactory : IHistoryViewModelFactory
{
    private readonly IServiceProvider _rootServiceProvider;

    public HistoryViewModelFactory(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider;
    }

    public CleanupSessionHistoryViewModel CreateCleanupSessionHistoryViewModel(Guid sessionId)
    {
        var scope = _rootServiceProvider.CreateScope();
        try
        {
            var repository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
            var navService = scope.ServiceProvider.GetRequiredService<INavigationService>();
            var errorBoundary = scope.ServiceProvider.GetRequiredService<IErrorBoundaryService>();
            var logger = scope.ServiceProvider.GetService<ILogger<CleanupSessionHistoryViewModel>>();

            return new CleanupSessionHistoryViewModel(
                errorBoundary,
                repository,
                navService,
                this,
                sessionId,
                scope,
                logger);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public RecoverySessionHistoryViewModel CreateRecoverySessionHistoryViewModel(Guid sessionId)
    {
        var scope = _rootServiceProvider.CreateScope();
        try
        {
            var repository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
            var navService = scope.ServiceProvider.GetRequiredService<INavigationService>();
            var errorBoundary = scope.ServiceProvider.GetRequiredService<IErrorBoundaryService>();
            var logger = scope.ServiceProvider.GetService<ILogger<RecoverySessionHistoryViewModel>>();

            return new RecoverySessionHistoryViewModel(
                errorBoundary,
                repository,
                navService,
                sessionId,
                scope,
                logger);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public ApplicationHistoryViewModel CreateApplicationHistoryViewModel(Guid applicationId, string applicationName)
    {
        var scope = _rootServiceProvider.CreateScope();
        try
        {
            var repository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
            var navService = scope.ServiceProvider.GetRequiredService<INavigationService>();
            var errorBoundary = scope.ServiceProvider.GetRequiredService<IErrorBoundaryService>();
            var logger = scope.ServiceProvider.GetService<ILogger<ApplicationHistoryViewModel>>();

            return new ApplicationHistoryViewModel(
                errorBoundary,
                repository,
                navService,
                this,
                applicationId,
                applicationName,
                scope,
                logger);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
