using System.Collections.ObjectModel;
using Uninstaller.App.Services;

namespace Uninstaller.App.ViewModels;

public partial class CleanupPlanViewModel : ViewModelBase
{
    public CleanupPlanViewModel(IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        Items = new ObservableCollection<CleanupItemViewModel>();
        State = Enums.UIState.Ready;
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; }
    
    // Explicitly NO Execution commands in Phase 5A as requested.
}
