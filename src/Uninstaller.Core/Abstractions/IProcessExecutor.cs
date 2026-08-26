using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Models;

namespace Uninstaller.Core.Abstractions;

public interface IProcessExecutor
{
    Task<ExecutionResult> ExecuteAsync(StructuredCommand command, CancellationToken cancellationToken = default);
}
