using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IApplicationRepository
{
    Task<IReadOnlyList<Application>> GetAllAsync(CancellationToken cancellationToken);
    Task<Application?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Application application, CancellationToken cancellationToken);
    Task<Uninstaller.Core.Models.SyncResult> SyncAsync(IEnumerable<Application> discoveredApps, CancellationToken cancellationToken);
}
