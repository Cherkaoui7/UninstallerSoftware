using System;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IReconciliationRepository
{
    Task<CleanupPlanItem?> GetCleanupItemAsync(Guid itemId, CancellationToken cancellationToken = default);
}
