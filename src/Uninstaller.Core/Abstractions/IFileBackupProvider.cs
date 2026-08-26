using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IFileBackupProvider
{
    Task<Backup> BackupFileSystemArtifactAsync(CleanupPlanItem item, string sessionBackupDirectory, CancellationToken cancellationToken = default);
    Task<BackupVerificationResult> VerifyFileSystemBackupAsync(Backup backup, CancellationToken cancellationToken = default);
    Task RestoreFileSystemBackupAsync(Backup backup, string destinationRoot, CancellationToken cancellationToken = default);
}
