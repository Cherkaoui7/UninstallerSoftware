using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Models;

namespace Uninstaller.Core.Abstractions;

public interface IDiscoveryService
{
    Task<DiscoveryResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default);
}
