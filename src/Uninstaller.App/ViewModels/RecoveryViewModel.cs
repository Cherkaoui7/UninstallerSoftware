using System.Collections.ObjectModel;
using Uninstaller.App.Services;

namespace Uninstaller.App.ViewModels;

public partial class RecoveryViewModel : ViewModelBase
{
    public RecoveryViewModel(IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        Sessions = new ObservableCollection<RecoverySessionViewModel>();
        State = Enums.UIState.Ready;
        StatusMessage = "Recovery history loaded.";
    }

    public ObservableCollection<RecoverySessionViewModel> Sessions { get; }
}
