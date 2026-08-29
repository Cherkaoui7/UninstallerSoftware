using System.Threading;
using System.Threading.Tasks;

namespace Uninstaller.Core.Abstractions;

public interface IStartupRecoveryService
{
    Task<bool> ReconcileIncompleteTransactionsAsync(CancellationToken cancellationToken = default);
}
