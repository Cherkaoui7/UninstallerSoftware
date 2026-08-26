namespace Uninstaller.Core.Abstractions;

public interface IFileSystemService
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
}
