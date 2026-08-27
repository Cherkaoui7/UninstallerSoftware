using Uninstaller.App.Services;

namespace Uninstaller.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        State = Enums.UIState.Ready;
        StatusMessage = "Settings";
    }
}
