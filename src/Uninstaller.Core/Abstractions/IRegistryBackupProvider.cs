using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IRegistryBackupProvider
{
    Task<Backup> BackupRegistryArtifactAsync(CleanupPlanItem item, string sessionBackupDirectory, CancellationToken cancellationToken = default);
    Task<BackupVerificationResult> VerifyRegistryBackupAsync(Backup backup, CancellationToken cancellationToken = default);
    Task RestoreRegistryBackupAsync(Backup backup, string testFixtureRoot, CancellationToken cancellationToken = default);
}
