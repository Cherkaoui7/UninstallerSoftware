using System.IO;
using Uninstaller.Core.Abstractions;

namespace Uninstaller.Windows.FileSystem;

public class WindowsFileSystemService : IFileSystemService
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }
}
