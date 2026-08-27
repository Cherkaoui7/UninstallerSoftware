using Uninstaller.App.Services;

namespace Uninstaller.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    public DashboardViewModel(IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        State = Enums.UIState.Ready;
        StatusMessage = "Welcome to Uninstaller";
    }
}
