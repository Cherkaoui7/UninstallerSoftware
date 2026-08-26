namespace Uninstaller.Domain.Enums;

public enum BackupStatus
{
    Pending,
    Writing,
    Verifying,
    Verified,
    Failed,
    Committed
}
