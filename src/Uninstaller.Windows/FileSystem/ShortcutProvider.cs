using System;
using System.Runtime.InteropServices;
using System.IO.Abstractions;

namespace Uninstaller.Windows.Filesystem;

public interface IShortcutProvider
{
    ShortcutInfo? GetShortcutInfo(string lnkPath);
}

public class ShortcutInfo
{
    public string TargetPath { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
}

// In a real application, you might use IWshRuntimeLibrary or IShellLink COM interface.
// For the purpose of safely resolving the target path without executing, we provide a placeholder wrapper.
// NOTE: For true parsing in .NET without COM, third party libraries or P/Invoke IShellLink is needed.
// We use a dummy implementation here that assumes the caller will use COM, but since we are strictly
// enforcing domain logic and testability, we just return empty or simulated for the sake of the exercise
// if COM isn't available, or we could use Type.GetTypeFromProgID("WScript.Shell").
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ShortcutProvider : IShortcutProvider
{
    private readonly IFileSystem _fileSystem;

    public ShortcutProvider(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public ShortcutInfo? GetShortcutInfo(string lnkPath)
    {
        try
        {
            if (!_fileSystem.File.Exists(lnkPath)) return null;

            // Using dynamic WScript.Shell to avoid adding COM references
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                return new ShortcutInfo
                {
                    TargetPath = shortcut.TargetPath,
                    Arguments = shortcut.Arguments,
                    WorkingDirectory = shortcut.WorkingDirectory
                };
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            return null; // Gracefully fail if WScript.Shell is missing or access is denied
        }
    }
}
