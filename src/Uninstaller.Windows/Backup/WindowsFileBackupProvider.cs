using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Backup;

public class DirectoryBackupManifest
{
    public string OriginalRoot { get; set; } = string.Empty;
    public List<FileBackupEntry> Files { get; set; } = new();
}

public class FileBackupEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Hash { get; set; } = string.Empty;
}

public class WindowsFileBackupProvider : IFileBackupProvider
{
    private readonly ICanonicalPathResolver _pathResolver;
    private readonly IBackupStorage _storage;

    public WindowsFileBackupProvider(ICanonicalPathResolver pathResolver, IBackupStorage storage)
    {
        _pathResolver = pathResolver;
        _storage = storage;
    }

    public Task<Uninstaller.Domain.Entities.Backup> BackupFileSystemArtifactAsync(CleanupPlanItem item, string sessionBackupDirectory, CancellationToken cancellationToken = default)
    {
        var backup = new Uninstaller.Domain.Entities.Backup
        {
            ArtifactId = Guid.NewGuid(),
            ArtifactType = item.ArtifactType,
            OriginalPath = item.Path,
            Status = BackupStatus.Pending
        };

        try
        {
            backup.Status = BackupStatus.Writing;

            var sourcePath = item.Path;
            var destName = backup.ArtifactId.ToString("N");
            var destPath = Path.Combine(sessionBackupDirectory, destName);

            if (!_storage.IsPathWithinControlledRoot(destPath))
            {
                throw new InvalidOperationException("Destination path escapes controlled backup root.");
            }

            if (item.ArtifactType == ArtifactType.File || item.ArtifactType == ArtifactType.Shortcut)
            {
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException("Source file not found.", sourcePath);
                }

                var attrs = File.GetAttributes(sourcePath);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException("Source file is a reparse point. Backups of reparse points are rejected.");
                }

                File.Copy(sourcePath, destPath, overwrite: false);

                var fileInfo = new FileInfo(destPath);
                backup.Size = fileInfo.Length;
                backup.Hash = ComputeSha256(destPath);
                backup.BackupPath = destPath;
                backup.Status = BackupStatus.Verifying;
            }
            else if (item.ArtifactType == ArtifactType.Directory)
            {
                if (!Directory.Exists(sourcePath))
                {
                    throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");
                }

                var manifestPath = destPath + "_manifest.json";
                var manifest = new DirectoryBackupManifest { OriginalRoot = sourcePath };

                Directory.CreateDirectory(destPath);
                long totalSize = 0;
                CopyDirectoryRecursively(sourcePath, destPath, sourcePath, manifest, ref totalSize);

                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

                backup.BackupPath = destPath;
                backup.Size = totalSize;
                backup.Status = BackupStatus.Verifying;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported artifact type for file backup: {item.ArtifactType}");
            }

            return Task.FromResult(backup);
        }
        catch (Exception ex)
        {
            backup.Status = BackupStatus.Failed;
            backup.FailureReason = ex.Message;
            return Task.FromResult(backup);
        }
    }

    private void CopyDirectoryRecursively(string sourceDir, string targetDir, string originalRoot, DirectoryBackupManifest manifest, ref long totalSize)
    {
        var attrs = File.GetAttributes(sourceDir);
        if (attrs.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"Directory {sourceDir} is a reparse point. Backups of reparse points are rejected.");
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileAttrs = File.GetAttributes(file);
            if (fileAttrs.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"File {file} is a reparse point. Backups of reparse points are rejected.");
            }

            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: false);

            var hash = ComputeSha256(destFile);
            var fileInfo = new FileInfo(destFile);
            totalSize += fileInfo.Length;

            manifest.Files.Add(new FileBackupEntry
            {
                RelativePath = Path.GetRelativePath(originalRoot, file),
                Size = fileInfo.Length,
                Hash = hash
            });
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            Directory.CreateDirectory(destSubDir);
            CopyDirectoryRecursively(subDir, destSubDir, originalRoot, manifest, ref totalSize);
        }
    }

    public Task<BackupVerificationResult> VerifyFileSystemBackupAsync(Uninstaller.Domain.Entities.Backup backup, CancellationToken cancellationToken = default)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));

        if (backup.Status == BackupStatus.Failed)
        {
            return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup already failed." });
        }

        try
        {
            if (!_storage.IsPathWithinControlledRoot(backup.BackupPath))
            {
                return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup path escapes controlled root." });
            }

            if (backup.ArtifactType == ArtifactType.File || backup.ArtifactType == ArtifactType.Shortcut)
            {
                if (!File.Exists(backup.BackupPath))
                {
                    return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup file is missing." });
                }

                var currentHash = ComputeSha256(backup.BackupPath);
                var currentSize = new FileInfo(backup.BackupPath).Length;

                if (currentHash != backup.Hash || currentSize != backup.Size)
                {
                    return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Integrity check failed (hash or size mismatch)." });
                }
            }
            else if (backup.ArtifactType == ArtifactType.Directory)
            {
                if (!Directory.Exists(backup.BackupPath))
                {
                    return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup directory is missing." });
                }

                var manifestPath = backup.BackupPath + "_manifest.json";
                if (!File.Exists(manifestPath))
                {
                    return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup manifest is missing." });
                }

                var manifestContent = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<DirectoryBackupManifest>(manifestContent);
                
                if (manifest == null || manifest.Files == null)
                {
                    return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup manifest is malformed." });
                }

                long computedSize = 0;
                foreach (var entry in manifest.Files)
                {
                    var entryPath = Path.Combine(backup.BackupPath, entry.RelativePath);
                    if (!File.Exists(entryPath))
                    {
                        return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = $"File missing in backup: {entry.RelativePath}" });
                    }
                    var currentHash = ComputeSha256(entryPath);
                    var currentSize = new FileInfo(entryPath).Length;

                    if (currentHash != entry.Hash || currentSize != entry.Size)
                    {
                        return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = $"Integrity check failed for file: {entry.RelativePath}" });
                    }
                    
                    computedSize += currentSize;
                }
                
                if (computedSize != backup.Size)
                {
                     return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Total size mismatch." });
                }
            }
            else
            {
                return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Unsupported artifact type." });
            }

            return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult
            {
                IsValid = true,
                Hash = backup.Hash,
                Size = backup.Size,
                VerifiedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = ex.Message });
        }
    }

    public Task RestoreFileSystemBackupAsync(Uninstaller.Domain.Entities.Backup backup, string destinationRoot, CancellationToken cancellationToken = default)
    {
        // Safe restoration primitive specifically for tests, not for direct execution engine yet
        // Restores the backup to the destination root, creating the original path mapping.
        
        var normalizedOriginal = backup.OriginalPath.Replace(":", "").TrimStart('\\', '/');
        var fullRestoreTarget = Path.Combine(destinationRoot, normalizedOriginal);

        var targetDir = Path.GetDirectoryName(fullRestoreTarget);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (backup.ArtifactType == ArtifactType.File || backup.ArtifactType == ArtifactType.Shortcut)
        {
            File.Copy(backup.BackupPath, fullRestoreTarget, overwrite: true);
        }
        else if (backup.ArtifactType == ArtifactType.Directory)
        {
            var manifestContent = File.ReadAllText(backup.BackupPath + "_manifest.json");
            var manifest = JsonSerializer.Deserialize<DirectoryBackupManifest>(manifestContent);
            
            if (manifest != null && manifest.Files != null)
            {
                foreach (var entry in manifest.Files)
                {
                    var sourceFile = Path.Combine(backup.BackupPath, entry.RelativePath);
                    var destFile = Path.Combine(fullRestoreTarget, entry.RelativePath);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(sourceFile, destFile, overwrite: true);
                }
            }
        }
        
        return Task.CompletedTask;
    }

    private string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
