using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Windows.Filesystem;

public class WindowsCanonicalPathResolver : ICanonicalPathResolver
{
    private static readonly Lazy<HashSet<string>> _recursivelyProtectedPaths = new Lazy<HashSet<string>>(GetRecursivelyProtectedPaths);
    private static readonly Lazy<HashSet<string>> _exactProtectedRoots = new Lazy<HashSet<string>>(GetExactProtectedRoots);
    private static readonly Lazy<HashSet<string>> _desktopDirectories = new Lazy<HashSet<string>>(GetDesktopDirectories);

    private static HashSet<string> GetRecursivelyProtectedPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        void AddFolderRecursive(Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(NormalizeLexically(path));
                }
            }
            catch { }
        }

        // 1. Windows OS subtrees (all contents protected)
        AddFolderRecursive(Environment.SpecialFolder.Windows);
        AddFolderRecursive(Environment.SpecialFolder.System);
        AddFolderRecursive(Environment.SpecialFolder.SystemX86);

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        paths.Add(NormalizeLexically($@"{systemDrive}\Windows"));
        paths.Add(NormalizeLexically($@"{systemDrive}\Recovery"));
        paths.Add(NormalizeLexically($@"{systemDrive}\$Recycle.Bin"));
        paths.Add(NormalizeLexically($@"{systemDrive}\System Volume Information"));
        paths.Add(NormalizeLexically($@"{systemDrive}\Boot"));
        paths.Add(NormalizeLexically($@"{systemDrive}\EFI"));

        // 2. Personal user-data subtrees (all personal documents and media protected)
        AddFolderRecursive(Environment.SpecialFolder.MyDocuments);
        AddFolderRecursive(Environment.SpecialFolder.MyPictures);
        AddFolderRecursive(Environment.SpecialFolder.MyVideos);
        AddFolderRecursive(Environment.SpecialFolder.MyMusic);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var normalizedUser = NormalizeLexically(userProfile);
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Documents")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Downloads")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Pictures")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Videos")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Music")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "OneDrive")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Dropbox")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "Google Drive")));
            paths.Add(NormalizeLexically(Path.Combine(normalizedUser, "iCloudDrive")));
        }

        return paths;
    }

    private static HashSet<string> GetExactProtectedRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRootExact(Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    roots.Add(NormalizeLexically(path));
                }
            }
            catch { }
        }

        // Exact container roots (the root folder itself must not be deleted, but app subdirectories inside are allowed)
        AddRootExact(Environment.SpecialFolder.UserProfile);
        AddRootExact(Environment.SpecialFolder.ProgramFiles);
        AddRootExact(Environment.SpecialFolder.ProgramFilesX86);
        AddRootExact(Environment.SpecialFolder.CommonApplicationData); // ProgramData
        AddRootExact(Environment.SpecialFolder.CommonProgramFiles);
        AddRootExact(Environment.SpecialFolder.CommonProgramFilesX86);
        AddRootExact(Environment.SpecialFolder.Desktop);
        AddRootExact(Environment.SpecialFolder.DesktopDirectory);
        AddRootExact(Environment.SpecialFolder.CommonDesktopDirectory);
        AddRootExact(Environment.SpecialFolder.ApplicationData); // Roaming
        AddRootExact(Environment.SpecialFolder.LocalApplicationData); // Local

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        roots.Add(NormalizeLexically(systemDrive + "\\"));
        roots.Add(NormalizeLexically(systemDrive));
        roots.Add(NormalizeLexically($@"{systemDrive}\Program Files"));
        roots.Add(NormalizeLexically($@"{systemDrive}\Program Files (x86)"));
        roots.Add(NormalizeLexically($@"{systemDrive}\ProgramData"));
        roots.Add(NormalizeLexically($@"{systemDrive}\Users"));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var normalizedUser = NormalizeLexically(userProfile);
            roots.Add(normalizedUser);
            roots.Add(NormalizeLexically(Path.Combine(normalizedUser, "AppData")));
            roots.Add(NormalizeLexically(Path.Combine(normalizedUser, "AppData", "Local")));
            roots.Add(NormalizeLexically(Path.Combine(normalizedUser, "AppData", "LocalLow")));
            roots.Add(NormalizeLexically(Path.Combine(normalizedUser, "AppData", "Roaming")));
            roots.Add(NormalizeLexically(Path.Combine(normalizedUser, "Desktop")));
            var usersDir = Path.GetDirectoryName(normalizedUser);
            if (!string.IsNullOrWhiteSpace(usersDir))
            {
                roots.Add(NormalizeLexically(usersDir));
            }
        }

        return roots;
    }

    private static HashSet<string> GetDesktopDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddDesktopSafe(Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    dirs.Add(NormalizeLexically(path));
                }
            }
            catch { }
        }

        AddDesktopSafe(Environment.SpecialFolder.Desktop);
        AddDesktopSafe(Environment.SpecialFolder.DesktopDirectory);
        AddDesktopSafe(Environment.SpecialFolder.CommonDesktopDirectory);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            dirs.Add(NormalizeLexically(Path.Combine(userProfile, "Desktop")));
        }

        return dirs;
    }

    private static string NormalizeLexically(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        
        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public bool IsPathContainedWithin(string path, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootPath)) return false;

        var normalizedPath = NormalizeLexically(path);
        var normalizedRoot = NormalizeLexically(rootPath);

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true; // Path equals root
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public PathSafetyResult ResolveAndVerify(string path, string? expectedRoot = null, CancellationToken cancellationToken = default)
    {
        var result = new PathSafetyResult
        {
            IsValid = false,
            IsCanonical = false,
            IsProtected = false,
            IsReparsePoint = false,
            IsWithinExpectedRoot = false,
            Reason = string.Empty,
            CanonicalPath = string.Empty
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            result.Reason = "Path is null or empty.";
            return result;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            result.Reason = "Path contains invalid characters.";
            return result;
        }
        
        if (!Path.IsPathRooted(path))
        {
            result.Reason = "Path must be an absolute path.";
            return result;
        }

        string canonical;
        try
        {
            canonical = Path.GetFullPath(path);
            canonical = canonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            result.CanonicalPath = canonical;
            result.IsCanonical = true;
        }
        catch (Exception ex)
        {
            result.Reason = $"Failed to get full path: {ex.Message}";
            return result;
        }

        // 1. Check Protected Roots and Subtrees
        // 1a. Drive root check
        var pathRoot = Path.GetPathRoot(canonical);
        if (string.Equals(canonical, pathRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(canonical, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            result.IsProtected = true;
            result.Warnings.Add($"Path is a drive root: {canonical}");
            return result;
        }

        // 1b. Exact protected roots (e.g. C:\Program Files, C:\Users\user, C:\Users\user\AppData\Roaming, etc.)
        if (_exactProtectedRoots.Value.Contains(canonical))
        {
            result.IsProtected = true;
            result.Warnings.Add($"Path is an exact protected system or container root: {canonical}");
        }
        else
        {
            // 1c. Desktop handling: allow application shortcut files (.lnk), protect user folders/files
            bool isDesktopPath = false;
            foreach (var desktopDir in _desktopDirectories.Value)
            {
                if (IsPathContainedWithin(canonical, desktopDir))
                {
                    isDesktopPath = true;
                    if (!canonical.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsProtected = true;
                        result.Warnings.Add($"Path is non-shortcut user data on Desktop: {canonical}");
                    }
                    break;
                }
            }

            // 1d. Recursively protected trees (e.g. C:\Windows, C:\Users\user\Documents, Downloads, etc.)
            if (!result.IsProtected && !isDesktopPath)
            {
                foreach (var protectedTree in _recursivelyProtectedPaths.Value)
                {
                    if (IsPathContainedWithin(canonical, protectedTree))
                    {
                        result.IsProtected = true;
                        result.Warnings.Add($"Path is within or equal to protected location: {protectedTree}");
                        break;
                    }
                }
            }
        }

        // 2. Check Expected Root Containment
        if (!string.IsNullOrWhiteSpace(expectedRoot))
        {
            try
            {
                var canonicalExpectedRoot = NormalizeLexically(expectedRoot);
                if (string.Equals(canonical, canonicalExpectedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsWithinExpectedRoot = true;
                    result.Warnings.Add("Path is exactly the expected root, not a child. Future executor must be cautious about deleting the root itself.");
                }
                else if (IsPathContainedWithin(canonical, canonicalExpectedRoot))
                {
                    result.IsWithinExpectedRoot = true;
                }
                else
                {
                    result.IsWithinExpectedRoot = false;
                    result.Warnings.Add($"Path is not contained within the expected root: {canonicalExpectedRoot}");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to normalize expected root: {ex.Message}");
            }
        }

        // 3. Reparse Point Check (up the tree)
        CheckReparsePoints(canonical, result);

        result.IsValid = true;
        result.Reason = "Path successfully resolved and analyzed.";

        return result;
    }

    private void CheckReparsePoints(string canonicalPath, PathSafetyResult result)
    {
        var current = canonicalPath;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                // Use File.GetAttributes which works for files and directories without opening handles
                if (File.Exists(current) || Directory.Exists(current))
                {
                    var attributes = File.GetAttributes(current);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        result.IsReparsePoint = true;
                        result.Warnings.Add($"Path segment '{current}' is a reparse point (symlink/junction). It is unsafe to process automatically.");
                        return; // Fail closed, stop traversing
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to check attributes for '{current}': {ex.Message}");
            }

            current = Path.GetDirectoryName(current);
        }
    }
}
