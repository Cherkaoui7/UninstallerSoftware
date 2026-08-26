namespace Uninstaller.Domain.Enums;

public enum RecoveryItemExecutionState
{
    Pending = 0,
    Validating = 1,
    VerifyingBackup = 2,
    Restoring = 3,
    Verifying = 4,
    Recovered = 5,
    Conflict = 6,
    Failed = 7,
    Cancelled = 8
}
