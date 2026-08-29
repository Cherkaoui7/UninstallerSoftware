using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class TransactionJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ItemId { get; set; }
    public TransactionType TransactionType { get; set; }
    public string State { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
