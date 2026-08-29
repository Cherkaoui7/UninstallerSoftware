using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Abstractions;

public interface ITransactionJournal
{
    Task RecordStateAsync(Guid sessionId, Guid itemId, TransactionType type, string state, CancellationToken cancellationToken = default);
    Task<IEnumerable<TransactionJournalEntry>> GetIncompleteTransactionsAsync(CancellationToken cancellationToken = default);
    Task UpdateEntryAsync(TransactionJournalEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransactionJournalEntry>> GetHistoryAsync(Guid sessionId, Guid itemId, CancellationToken cancellationToken = default);
}
