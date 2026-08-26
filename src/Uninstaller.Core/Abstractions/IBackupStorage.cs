using System;

namespace Uninstaller.Core.Abstractions;

public interface IBackupStorage
{
    /// <summary>
    /// Gets the root path for all backups (e.g. %LocalAppData%\Uninstaller\Backups).
    /// </summary>
    string GetBackupRoot();

    /// <summary>
    /// Creates and returns a session-specific backup directory path.
    /// </summary>
    string GetOrCreateSessionDirectory(Guid sessionId);

    /// <summary>
    /// Verifies that a destination backup path does not escape the controlled root.
    /// </summary>
    bool IsPathWithinControlledRoot(string backupPath);
}
