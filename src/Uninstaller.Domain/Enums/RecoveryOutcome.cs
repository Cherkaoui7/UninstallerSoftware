namespace Uninstaller.Domain.Enums;

public enum RecoveryOutcome
{
    Recovered = 0,
    BackupInvalid = 1,
    RecoveryConflict = 2,
    AccessDenied = 3,
    Locked = 4,
    ValidationFailed = 5,
    VerificationFailed = 6,
    Failed = 7
}
