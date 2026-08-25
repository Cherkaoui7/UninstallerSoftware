namespace Uninstaller.Domain.Enums;

public enum UninstallSessionStatus
{
    Created,
    Analyzing,
    Uninstalling,
    Scanning,
    WaitingForConfirmation,
    BackingUp,
    Executing,
    Verifying,
    Completed,
    Failed,
    RollingBack,
    RolledBack,
    PartiallyCompleted,
    Cancelled
}
