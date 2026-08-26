using System;
using Uninstaller.Core.Abstractions;
using Uninstaller.Windows.Filesystem;

namespace Uninstaller.Windows.FileSystem;

public class WindowsShortcutService : IShortcutService
{
    private readonly IShortcutProvider _shortcutProvider;
    private readonly IFileSystemService _fileSystem;

    public WindowsShortcutService(IShortcutProvider shortcutProvider, IFileSystemService fileSystem)
    {
        _shortcutProvider = shortcutProvider;
        _fileSystem = fileSystem;
    }

    public bool ShortcutExists(string path)
    {
        return _fileSystem.FileExists(path);
    }

    public string GetShortcutTarget(string path)
    {
        var info = _shortcutProvider.GetShortcutInfo(path);
        return info?.TargetPath ?? string.Empty;
    }
}
