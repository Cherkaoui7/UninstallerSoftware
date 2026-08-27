using CommunityToolkit.Mvvm.ComponentModel;

namespace Uninstaller.App.ViewModels;

public partial class RecoverySessionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _backupName = string.Empty;

    [ObservableProperty]
    private string _originalArtifact = string.Empty;

    [ObservableProperty]
    private string _verificationState = string.Empty;

    [ObservableProperty]
    private string _recoveryState = string.Empty;

    [ObservableProperty]
    private string _conflictStatus = string.Empty;

    [ObservableProperty]
    private string _failureReason = string.Empty;
}
