using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.App.ViewModels;

public partial class RecoveryItemViewModel : ObservableObject
{
    public Backup Backup { get; }

    public RecoveryItemViewModel(Backup backup)
    {
        Backup = backup ?? throw new ArgumentNullException(nameof(backup));
        IsSelected = IsRecoverable;
        State = RecoveryItemExecutionState.Pending;
    }

    public Guid BackupId => Backup.Id;
    public Guid ArtifactId => Backup.ArtifactId;
    public ArtifactType ArtifactType => Backup.ArtifactType;
    public string OriginalPath => Backup.OriginalPath;
    public BackupVerificationStatus BackupVerificationStatus => Backup.VerificationStatus;
    
    [ObservableProperty]
    private RecoveryItemExecutionState _state;

    [ObservableProperty]
    private RecoveryOutcome? _outcome;

    [ObservableProperty]
    private string? _failureReason;

    [ObservableProperty]
    private bool _isSelected;

    public bool IsRecoverable
    {
        get
        {
            if (BackupVerificationStatus != BackupVerificationStatus.Verified)
                return false;

            if (ArtifactType == ArtifactType.Other)
                return false;

            // If a previous persisted failure exists on the backup, treat it as blocking
            if (!string.IsNullOrEmpty(Backup.FailureReason))
                return false;

            return true;
        }
    }

    public string RecoveryStatus
    {
        get
        {
            if (State == RecoveryItemExecutionState.Pending) return "Pending";
            if (State == RecoveryItemExecutionState.Validating) return "Validating...";
            if (State == RecoveryItemExecutionState.VerifyingBackup) return "Verifying Backup...";
            if (State == RecoveryItemExecutionState.Restoring) return "Restoring...";
            if (State == RecoveryItemExecutionState.Verifying) return "Verifying...";
            if (State == RecoveryItemExecutionState.Recovered) return "Recovered";
            if (State == RecoveryItemExecutionState.Conflict) return "Conflict";
            if (State == RecoveryItemExecutionState.Failed) return "Failed";
            if (State == RecoveryItemExecutionState.Cancelled) return "Cancelled";
            return State.ToString();
        }
    }

    public string? ConflictStatus => Outcome == RecoveryOutcome.RecoveryConflict ? "Conflict detected" : null;

    partial void OnIsSelectedChanged(bool value)
    {
        // Prevent selection of unrecoverable items
        if (value && !IsRecoverable)
        {
            IsSelected = false;
        }
    }

    partial void OnStateChanged(RecoveryItemExecutionState value)
    {
        OnPropertyChanged(nameof(RecoveryStatus));
    }

    partial void OnOutcomeChanged(RecoveryOutcome? value)
    {
        OnPropertyChanged(nameof(ConflictStatus));
    }
}
