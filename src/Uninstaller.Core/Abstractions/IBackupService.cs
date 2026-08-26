using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IBackupService
{
    Task<BackupManifest> CreateBackupManifestAsync(CleanupPlan plan, CancellationToken cancellationToken = default);
    Task<BackupVerificationResult> VerifyBackupManifestAsync(BackupManifest manifest, CancellationToken cancellationToken = default);
    Task<Backup> BackupArtifactAsync(CleanupPlanItem item, Guid sessionId, CancellationToken cancellationToken = default);
    Task<BackupVerificationResult> VerifyBackupAsync(Backup backup, CancellationToken cancellationToken = default);
}
