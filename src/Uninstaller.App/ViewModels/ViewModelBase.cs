using CommunityToolkit.Mvvm.ComponentModel;
using Uninstaller.App.Enums;
using Uninstaller.App.Services;
using System.Threading;

namespace Uninstaller.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected readonly IErrorBoundaryService ErrorBoundary;

    protected ViewModelBase(IErrorBoundaryService errorBoundary)
    {
        ErrorBoundary = errorBoundary;
        State = UIState.Ready;
    }

    [ObservableProperty]
    private UIState _state;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;
    
    protected void SetError(string message)
    {
        State = UIState.Error;
        ErrorMessage = message;
    }
    
    protected void SetWarning(string message)
    {
        State = UIState.Warning;
        ErrorMessage = message;
    }
    
    protected void SetWorking(string message)
    {
        State = UIState.Working;
        StatusMessage = message;
        ErrorMessage = null;
    }
}
