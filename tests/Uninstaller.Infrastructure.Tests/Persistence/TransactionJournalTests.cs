using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Uninstaller.Infrastructure.Tests.Persistence;

public class TransactionJournalTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly TransactionJournal _journal;

    public TransactionJournalTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _journal = new TransactionJournal(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task RecordStateAsync_WritesStateAndSequence()
    {
        var sessionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, "Executing");
        var entries = await _context.TransactionJournalEntries.ToListAsync();

        entries.Should().ContainSingle();
        entries[0].SessionId.Should().Be(sessionId);
        entries[0].ItemId.Should().Be(itemId);
        entries[0].TransactionType.Should().Be(TransactionType.Cleanup);
        entries[0].State.Should().Be("Executing");
        entries[0].SequenceNumber.Should().Be(1);
    }

    [Fact]
    public async Task RecordStateAsync_IncrementsSequenceNumber()
    {
        var sessionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, "Executing");
        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, "Failed");

        var entries = await _context.TransactionJournalEntries.OrderBy(e => e.SequenceNumber).ToListAsync();

        entries.Should().HaveCount(2);
        entries[0].State.Should().Be("Executing");
        entries[0].SequenceNumber.Should().Be(1);
        entries[1].State.Should().Be("Failed");
        entries[1].SequenceNumber.Should().Be(2);
    }

    [Fact]
    public async Task GetIncompleteTransactionsAsync_ReturnsNonTerminalStatesOnly()
    {
        var session1 = Guid.NewGuid();
        var item1 = Guid.NewGuid(); // Terminal (Succeeded)
        
        var session2 = Guid.NewGuid();
        var item2 = Guid.NewGuid(); // Non-terminal (Executing)

        var session3 = Guid.NewGuid();
        var item3 = Guid.NewGuid(); // Terminal (Recovered)

        await _journal.RecordStateAsync(session1, item1, TransactionType.Cleanup, "Executing");
        await _journal.RecordStateAsync(session1, item1, TransactionType.Cleanup, "Succeeded");

        await _journal.RecordStateAsync(session2, item2, TransactionType.Cleanup, "Pending");
        await _journal.RecordStateAsync(session2, item2, TransactionType.Cleanup, "Executing");

        await _journal.RecordStateAsync(session3, item3, TransactionType.Recovery, "Restoring");
        await _journal.RecordStateAsync(session3, item3, TransactionType.Recovery, "Recovered");

        var incomplete = await _journal.GetIncompleteTransactionsAsync();

        incomplete.Should().ContainSingle();
        incomplete.First().ItemId.Should().Be(item2);
        incomplete.First().State.Should().Be("Executing");
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsOrderedHistory()
    {
        var sessionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, "Pending");
        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, "Executing");
        await _journal.RecordStateAsync(sessionId, itemId, TransactionType.Cleanup, "Succeeded");

        var history = await _journal.GetHistoryAsync(sessionId, itemId);

        history.Should().HaveCount(3);
        history[0].State.Should().Be("Pending");
        history[1].State.Should().Be("Executing");
        history[2].State.Should().Be("Succeeded");
    }
}
