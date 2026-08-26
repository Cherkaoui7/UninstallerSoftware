namespace Uninstaller.Domain.Enums;

public enum CleanupItemExecutionState
{
    Pending,
    Validating,
    PreflightAuthorized,
    BackingUp,
    BackupVerified,
    FinalValidating,
    Executing,
    Verifying,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}
