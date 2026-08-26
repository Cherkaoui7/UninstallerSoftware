using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface ICleanupTransactionEngine
{
    Task<CleanupSessionResult> ExecuteAsync(
        CleanupPlan plan,
        Application application,
        IEnumerable<Guid> selectedItemIds,
        CancellationToken cancellationToken = default);
}
