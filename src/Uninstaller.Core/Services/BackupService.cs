using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class BackupService : IBackupService
{
    private readonly IBackupStorage _storage;
    private readonly IFileBackupProvider _fileBackupProvider;
    private readonly IRegistryBackupProvider _registryBackupProvider;

    public BackupService(
        IBackupStorage storage,
        IFileBackupProvider fileBackupProvider,
        IRegistryBackupProvider registryBackupProvider)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _fileBackupProvider = fileBackupProvider ?? throw new ArgumentNullException(nameof(fileBackupProvider));
        _registryBackupProvider = registryBackupProvider ?? throw new ArgumentNullException(nameof(registryBackupProvider));
    }

    public async Task<BackupManifest> CreateBackupManifestAsync(CleanupPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        var sessionDirectory = _storage.GetOrCreateSessionDirectory(plan.UninstallSessionId);
        
        var manifest = new BackupManifest
        {
            SessionId = plan.UninstallSessionId,
            CreatedAt = DateTime.UtcNow,
            ManifestVersion = "1.0",
            Backups = new List<Backup>()
        };

        foreach (var item in plan.Items)
        {
            if (!item.Recommended) continue; // Only backup recommended items for now, or maybe all items? Let's backup items we are planning to clean.

            Backup backup = null;
            try
            {
                if (item.ArtifactType == ArtifactType.Directory || item.ArtifactType == ArtifactType.File || item.ArtifactType == ArtifactType.Shortcut)
                {
                    backup = await _fileBackupProvider.BackupFileSystemArtifactAsync(item, sessionDirectory, cancellationToken);
                }
                else if (item.ArtifactType == ArtifactType.RegistryKey || item.ArtifactType == ArtifactType.RegistryValue)
                {
                    backup = await _registryBackupProvider.BackupRegistryArtifactAsync(item, sessionDirectory, cancellationToken);
                }
                else
                {
                    // Unsupported, mark failed
                    backup = new Backup
                    {
                        SessionId = plan.UninstallSessionId,
                        ArtifactType = item.ArtifactType,
                        OriginalPath = item.Path,
                        Status = BackupStatus.Failed,
                        FailureReason = $"Unsupported artifact type {item.ArtifactType}",
                        VerificationStatus = BackupVerificationStatus.Failed
                    };
                }
            }
            catch (Exception ex)
            {
                backup = new Backup
                {
                    SessionId = plan.UninstallSessionId,
                    ArtifactType = item.ArtifactType,
                    OriginalPath = item.Path,
                    Status = BackupStatus.Failed,
                    FailureReason = ex.Message,
                    VerificationStatus = BackupVerificationStatus.Failed
                };
            }

            if (backup != null)
            {
                manifest.Backups.Add(backup);
            }
        }

        return manifest;
    }

    public async Task<BackupVerificationResult> VerifyBackupManifestAsync(BackupManifest manifest, CancellationToken cancellationToken = default)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));

        bool allValid = true;
        foreach (var backup in manifest.Backups)
        {
            if (backup.Status == BackupStatus.Failed)
            {
                allValid = false;
                continue;
            }

            BackupVerificationResult result;
            if (backup.ArtifactType == ArtifactType.Directory || backup.ArtifactType == ArtifactType.File || backup.ArtifactType == ArtifactType.Shortcut)
            {
                result = await _fileBackupProvider.VerifyFileSystemBackupAsync(backup, cancellationToken);
            }
            else if (backup.ArtifactType == ArtifactType.RegistryKey || backup.ArtifactType == ArtifactType.RegistryValue)
            {
                result = await _registryBackupProvider.VerifyRegistryBackupAsync(backup, cancellationToken);
            }
            else
            {
                result = new BackupVerificationResult { IsValid = false, FailureReason = "Unsupported type" };
            }

            if (result.IsValid)
            {
                backup.VerificationStatus = BackupVerificationStatus.Verified;
                backup.Status = BackupStatus.Committed;
            }
            else
            {
                backup.VerificationStatus = BackupVerificationStatus.Failed;
                backup.Status = BackupStatus.Failed;
                backup.FailureReason = result.FailureReason;
                allValid = false;
            }
        }

        return new BackupVerificationResult
        {
            IsValid = allValid,
            VerifiedAt = DateTime.UtcNow
        };
    }

    public async Task<Backup> BackupArtifactAsync(CleanupPlanItem item, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        var sessionDirectory = _storage.GetOrCreateSessionDirectory(sessionId);
        
        try
        {
            if (item.ArtifactType == ArtifactType.Directory || item.ArtifactType == ArtifactType.File || item.ArtifactType == ArtifactType.Shortcut)
            {
                return await _fileBackupProvider.BackupFileSystemArtifactAsync(item, sessionDirectory, cancellationToken);
            }
            else if (item.ArtifactType == ArtifactType.RegistryKey || item.ArtifactType == ArtifactType.RegistryValue)
            {
                return await _registryBackupProvider.BackupRegistryArtifactAsync(item, sessionDirectory, cancellationToken);
            }
            else
            {
                return new Backup
                {
                    SessionId = sessionId,
                    ArtifactType = item.ArtifactType,
                    OriginalPath = item.Path,
                    Status = BackupStatus.Failed,
                    FailureReason = $"Unsupported artifact type {item.ArtifactType}",
                    VerificationStatus = BackupVerificationStatus.Failed
                };
            }
        }
        catch (Exception ex)
        {
            return new Backup
            {
                SessionId = sessionId,
                ArtifactType = item.ArtifactType,
                OriginalPath = item.Path,
                Status = BackupStatus.Failed,
                FailureReason = ex.Message,
                VerificationStatus = BackupVerificationStatus.Failed
            };
        }
    }

    public async Task<BackupVerificationResult> VerifyBackupAsync(Backup backup, CancellationToken cancellationToken = default)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));

        if (backup.Status == BackupStatus.Failed)
        {
            return new BackupVerificationResult { IsValid = false, FailureReason = backup.FailureReason };
        }

        BackupVerificationResult result;
        if (backup.ArtifactType == ArtifactType.Directory || backup.ArtifactType == ArtifactType.File || backup.ArtifactType == ArtifactType.Shortcut)
        {
            result = await _fileBackupProvider.VerifyFileSystemBackupAsync(backup, cancellationToken);
        }
        else if (backup.ArtifactType == ArtifactType.RegistryKey || backup.ArtifactType == ArtifactType.RegistryValue)
        {
            result = await _registryBackupProvider.VerifyRegistryBackupAsync(backup, cancellationToken);
        }
        else
        {
            result = new BackupVerificationResult { IsValid = false, FailureReason = "Unsupported type" };
        }

        if (result.IsValid)
        {
            backup.VerificationStatus = BackupVerificationStatus.Verified;
            backup.Status = BackupStatus.Committed;
        }
        else
        {
            backup.VerificationStatus = BackupVerificationStatus.Failed;
            backup.Status = BackupStatus.Failed;
            backup.FailureReason = result.FailureReason;
        }

        return result;
    }


    public Task<Backup?> GetBackupAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        // To be implemented when a persistence layer (e.g., manifest reader or database) is added.
        throw new NotImplementedException();
    }
}
