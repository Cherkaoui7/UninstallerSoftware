namespace Uninstaller.Core.Abstractions;

public interface IShortcutService
{
    bool ShortcutExists(string path);
    string GetShortcutTarget(string path);
}
