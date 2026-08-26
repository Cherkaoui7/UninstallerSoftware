using CommunityToolkit.Mvvm.ComponentModel;
using Uninstaller.Core.Models;
using Uninstaller.Domain.Entities;

namespace Uninstaller.App.ViewModels;

public partial class UninstallConfirmationViewModel : ObservableObject
{
    public UninstallConfirmationViewModel(Application application, StructuredCommand command)
    {
        ApplicationName = application.Name;
        Version = application.Version;
        Publisher = application.Publisher;
        
        ExecutionMethod = command.ExecutionType.ToString();
        ExecutablePath = command.ExecutablePath;
        Arguments = command.Arguments;
        RequiresElevation = command.RequiresElevation;
    }

    public string ApplicationName { get; }
    public string? Version { get; }
    public string? Publisher { get; }
    
    public string ExecutionMethod { get; }
    public string? ExecutablePath { get; }
    public string? Arguments { get; }
    public bool RequiresElevation { get; }
}
