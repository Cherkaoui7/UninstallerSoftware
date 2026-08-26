using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface ICleanupExecutor
{
    Task<CleanupExecutionResult> ExecuteAsync(AuthorizedExecutionContext context, CancellationToken cancellationToken = default);
}
