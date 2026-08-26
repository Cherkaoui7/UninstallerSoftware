namespace Uninstaller.Domain.Enums;

public enum UninstallSessionStatus
{
    Created,
    Validating,
    ReadyToExecute,
    Executing,
    ProcessCompleted,
    Verifying,
    Completed,
    Failed,
    Cancelled
}
