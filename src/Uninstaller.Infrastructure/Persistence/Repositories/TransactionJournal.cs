using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Infrastructure.Persistence.Repositories;

public class TransactionJournal : ITransactionJournal
{
    private readonly AppDbContext _context;

    public TransactionJournal(AppDbContext context)
    {
        _context = context;
    }

    public async Task RecordStateAsync(Guid sessionId, Guid itemId, TransactionType type, string state, CancellationToken cancellationToken = default)
    {
        long maxSeq = await _context.TransactionJournalEntries
            .Where(e => e.ItemId == itemId)
            .MaxAsync(e => (long?)e.SequenceNumber, cancellationToken) ?? 0;

        var entry = new TransactionJournalEntry
        {
            SessionId = sessionId,
            ItemId = itemId,
            TransactionType = type,
            State = state,
            SequenceNumber = maxSeq + 1,
            Timestamp = DateTime.UtcNow
        };

        _context.TransactionJournalEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<TransactionJournalEntry>> GetIncompleteTransactionsAsync(CancellationToken cancellationToken = default)
    {
        // To find incomplete items, we need the LATEST entry for each ItemId
        // If the latest state is not a terminal state, it's incomplete.
        var latestEntries = await _context.TransactionJournalEntries
            .GroupBy(e => e.ItemId)
            .Select(g => g.OrderByDescending(e => e.SequenceNumber).First())
            .ToListAsync(cancellationToken);

        var incomplete = latestEntries.Where(e => !IsTerminalState(e)).ToList();
        return incomplete;
    }

    public async Task UpdateEntryAsync(TransactionJournalEntry entry, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TransactionJournalEntries.FindAsync(new object[] { entry.Id }, cancellationToken);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(entry);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private bool IsTerminalState(TransactionJournalEntry entry)
    {
        if (entry.TransactionType == TransactionType.Cleanup)
        {
            if (Enum.TryParse<CleanupItemExecutionState>(entry.State, out var state))
            {
                return state == CleanupItemExecutionState.Succeeded ||
                       state == CleanupItemExecutionState.Failed ||
                       state == CleanupItemExecutionState.Skipped ||
                       state == CleanupItemExecutionState.Cancelled ||
                       state == CleanupItemExecutionState.Reconciled;
            }
        }
        else if (entry.TransactionType == TransactionType.Recovery)
        {
            if (Enum.TryParse<RecoveryItemExecutionState>(entry.State, out var state))
            {
                return state == RecoveryItemExecutionState.Recovered ||
                       state == RecoveryItemExecutionState.Failed ||
                       state == RecoveryItemExecutionState.Conflict ||
                       state == RecoveryItemExecutionState.Cancelled ||
                       state == RecoveryItemExecutionState.Reconciled;
            }
        }
        
        
        return false;
    }

    public async Task<IReadOnlyList<TransactionJournalEntry>> GetHistoryAsync(Guid sessionId, Guid itemId, CancellationToken cancellationToken = default)
    {
        return await _context.TransactionJournalEntries
            .Where(e => e.SessionId == sessionId && e.ItemId == itemId)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(cancellationToken);
    }
}
