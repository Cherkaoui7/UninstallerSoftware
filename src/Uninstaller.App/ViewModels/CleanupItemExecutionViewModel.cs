using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.App.ViewModels;

public partial class CleanupItemExecutionViewModel : ObservableObject
{
    private readonly CleanupPlanItem _item;

    public CleanupItemExecutionViewModel(CleanupPlanItem item)
    {
        _item = item;
        Id = item.Id;
        ArtifactType = item.ArtifactType;
        Path = item.Path;
        State = CleanupItemExecutionState.Pending;
    }

    public Guid Id { get; }
    public ArtifactType ArtifactType { get; }
    public string Path { get; }

    [ObservableProperty]
    private CleanupItemExecutionState _state;

    [ObservableProperty]
    private CleanupOutcome? _outcome;

    [ObservableProperty]
    private string? _failureReason;

    public string DisplayState
    {
        get
        {
            return State switch
            {
                CleanupItemExecutionState.Pending => "Waiting...",
                CleanupItemExecutionState.Validating => "Validating...",
                CleanupItemExecutionState.PreflightAuthorized => "Authorized",
                CleanupItemExecutionState.BackingUp => "Creating backup...",
                CleanupItemExecutionState.BackupVerified => "Backup verified",
                CleanupItemExecutionState.FinalValidating => "Final safety check...",
                CleanupItemExecutionState.Executing => "Removing...",
                CleanupItemExecutionState.Verifying => "Verifying...",
                CleanupItemExecutionState.Succeeded => "Success",
                CleanupItemExecutionState.Failed => "Failed",
                CleanupItemExecutionState.Skipped => "Skipped",
                CleanupItemExecutionState.Cancelled => "Cancelled",
                _ => State.ToString()
            };
        }
    }

    partial void OnStateChanged(CleanupItemExecutionState value)
    {
        OnPropertyChanged(nameof(DisplayState));
    }
}
