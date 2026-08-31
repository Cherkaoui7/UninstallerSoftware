using System;
using Microsoft.Extensions.DependencyInjection;
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

            return new CleanupSessionHistoryViewModel(
                errorBoundary,
                repository,
                navService,
                sessionId,
                scope);
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

            return new RecoverySessionHistoryViewModel(
                errorBoundary,
                repository,
                navService,
                sessionId,
                scope);
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

            return new ApplicationHistoryViewModel(
                errorBoundary,
                repository,
                navService,
                applicationId,
                applicationName,
                scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
