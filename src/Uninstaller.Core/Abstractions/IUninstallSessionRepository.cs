using System;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IUninstallSessionRepository
{
    Task<UninstallSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<UninstallSession?> GetLatestByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken);
    Task SaveAsync(UninstallSession session, CancellationToken cancellationToken);
}
