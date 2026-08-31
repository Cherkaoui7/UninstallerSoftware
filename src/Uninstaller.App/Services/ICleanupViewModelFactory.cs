using System;
using System.Collections.Generic;
using Uninstaller.App.ViewModels;
using Uninstaller.Domain.Entities;

namespace Uninstaller.App.Services;

public interface ICleanupViewModelFactory
{
    CleanupPlanViewModel CreatePlanViewModel(CleanupPlan plan, Application application);
    CleanupExecutionViewModel CreateExecutionViewModel(CleanupPlan plan, Application application, IEnumerable<Guid> selectedItemIds);
}
