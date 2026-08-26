using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IRecoveryTransactionEngine
{
    Task<RecoverySessionResult> ExecuteAsync(
        RecoverySession session,
        Application application,
        CancellationToken cancellationToken = default);
}
