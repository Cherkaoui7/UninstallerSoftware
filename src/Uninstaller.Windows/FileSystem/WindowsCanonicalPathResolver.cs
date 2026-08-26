using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Windows.Filesystem;

public class WindowsCanonicalPathResolver : ICanonicalPathResolver
{
    private static readonly Lazy<HashSet<string>> _protectedPaths = new Lazy<HashSet<string>>(GetProtectedPaths);

    private static HashSet<string> GetProtectedPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        void AddPathSafe(Environment.SpecialFolder folder)
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

        AddPathSafe(Environment.SpecialFolder.Windows);
        AddPathSafe(Environment.SpecialFolder.System);
        AddPathSafe(Environment.SpecialFolder.SystemX86);
        AddPathSafe(Environment.SpecialFolder.ProgramFiles);
        AddPathSafe(Environment.SpecialFolder.ProgramFilesX86);
        AddPathSafe(Environment.SpecialFolder.CommonApplicationData);
        AddPathSafe(Environment.SpecialFolder.MyDocuments);
        AddPathSafe(Environment.SpecialFolder.UserProfile);
        AddPathSafe(Environment.SpecialFolder.Desktop);
        AddPathSafe(Environment.SpecialFolder.MyPictures);
        AddPathSafe(Environment.SpecialFolder.MyVideos);
        AddPathSafe(Environment.SpecialFolder.MyMusic);

        // Explicit fallback for typically protected folders if they fail to resolve via SpecialFolder
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        paths.Add($@"{systemDrive}\Windows");
        paths.Add($@"{systemDrive}\Program Files");
        paths.Add($@"{systemDrive}\Program Files (x86)");

        return paths;
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

        // 1. Check Protected Paths
        foreach (var protectedRoot in _protectedPaths.Value)
        {
            if (IsPathContainedWithin(canonical, protectedRoot))
            {
                result.IsProtected = true;
                result.Warnings.Add($"Path is within or equal to protected system location: {protectedRoot}");
                break; // One protection is enough
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
