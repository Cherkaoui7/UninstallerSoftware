using Uninstaller.App.Services;

namespace Uninstaller.App.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    public HistoryViewModel(IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        State = Enums.UIState.Ready;
        StatusMessage = "History loaded.";
    }
}
