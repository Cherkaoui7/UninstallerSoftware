using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Models.History;

namespace Uninstaller.Core.Abstractions;

public interface IHistoryRepository
{
    Task<IReadOnlyList<HistoryActivity>> GetRecentActivitiesAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimelineEvent>> GetApplicationTimelineAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<HistoryActivity?> GetCleanupSessionDetailsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<HistoryActivity?> GetRecoverySessionDetailsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryItemDetail>> GetSessionItemDetailsAsync(Guid sessionId, ActivityType type, CancellationToken cancellationToken = default);
}
