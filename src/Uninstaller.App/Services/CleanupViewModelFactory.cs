using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Uninstaller.App.ViewModels;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;

namespace Uninstaller.App.Services;

public class CleanupViewModelFactory : ICleanupViewModelFactory
{
    private readonly IServiceProvider _rootServiceProvider;

    public CleanupViewModelFactory(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider;
    }

    public CleanupPlanViewModel CreatePlanViewModel(CleanupPlan plan, Application application)
    {
        var navService = _rootServiceProvider.GetRequiredService<INavigationService>();
        var errorBoundary = _rootServiceProvider.GetRequiredService<IErrorBoundaryService>();
        return new CleanupPlanViewModel(plan, application, navService, this, errorBoundary);
    }

    public CleanupExecutionViewModel CreateExecutionViewModel(CleanupPlan plan, Application application, IEnumerable<Guid> selectedItemIds)
    {
        var scope = _rootServiceProvider.CreateScope();
        try
        {
            var transactionEngine = scope.ServiceProvider.GetRequiredService<ICleanupTransactionEngine>();
            var tracker = scope.ServiceProvider.GetRequiredService<IObservableItemExecutionTracker>();
            var navService = scope.ServiceProvider.GetRequiredService<INavigationService>();
            var errorBoundary = scope.ServiceProvider.GetRequiredService<IErrorBoundaryService>();
            var logger = scope.ServiceProvider.GetService<ILogger<CleanupExecutionViewModel>>();

            return new CleanupExecutionViewModel(
                plan,
                application,
                selectedItemIds,
                transactionEngine,
                tracker,
                navService,
                errorBoundary,
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
