using System;
using System.IO;
using Uninstaller.Core.Abstractions;

namespace Uninstaller.Windows.Backup;

public class WindowsBackupStorage : IBackupStorage
{
    private readonly string _backupRoot;

    public WindowsBackupStorage()
    {
        // %LocalAppData%\Uninstaller\Backups
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _backupRoot = Path.Combine(localAppData, "Uninstaller", "Backups");
    }

    public string GetBackupRoot()
    {
        return _backupRoot;
    }

    public string GetOrCreateSessionDirectory(Guid sessionId)
    {
        var sessionDir = Path.Combine(_backupRoot, sessionId.ToString("N"));
        if (!Directory.Exists(sessionDir))
        {
            Directory.CreateDirectory(sessionDir);
        }
        return sessionDir;
    }

    public bool IsPathWithinControlledRoot(string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath)) return false;
        
        var fullRoot = Path.GetFullPath(_backupRoot);
        var fullPath = Path.GetFullPath(backupPath);

        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
