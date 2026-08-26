using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IResidualScanner
{
    string Name { get; }
    Task<IReadOnlyList<ResidualArtifactCandidate>> ScanAsync(Application application, CancellationToken cancellationToken);
}
