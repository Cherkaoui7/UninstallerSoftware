namespace Uninstaller.Domain.Enums;

public enum OperationType
{
    DeleteFile,
    DeleteDirectory,
    DeleteRegistryKey,
    StopService,
    DeleteService,
    DeleteScheduledTask,
    DeleteShortcut,
    RestoreFile,
    RestoreRegistryKey
}
