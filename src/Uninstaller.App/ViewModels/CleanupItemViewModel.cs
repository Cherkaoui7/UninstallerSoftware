using CommunityToolkit.Mvvm.ComponentModel;

namespace Uninstaller.App.ViewModels;

public partial class CleanupItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _artifactPath = string.Empty;

    [ObservableProperty]
    private string _artifactType = string.Empty;

    [ObservableProperty]
    private string _classification = string.Empty;

    [ObservableProperty]
    private string _confidence = string.Empty;

    [ObservableProperty]
    private string _risk = string.Empty;

    [ObservableProperty]
    private string _recommendation = string.Empty;

    [ObservableProperty]
    private bool _isProtected;

    [ObservableProperty]
    private string _reasons = string.Empty;

    [ObservableProperty]
    private string _appliedRules = string.Empty;
}
