using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class StartupRecoveryServiceTests
{
    private readonly Mock<ITransactionJournal> _journalMock;
    private readonly Mock<IReconciliationRepository> _repositoryMock;
    private readonly Mock<IFileSystemService> _fileSystemMock;
    private readonly Mock<IRegistryService> _registryMock;
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<ICanonicalPathResolver> _pathResolverMock;
    private readonly StartupRecoveryService _service;

    public StartupRecoveryServiceTests()
    {
        _journalMock = new Mock<ITransactionJournal>();
        _repositoryMock = new Mock<IReconciliationRepository>();
        _fileSystemMock = new Mock<IFileSystemService>();
        _registryMock = new Mock<IRegistryService>();
        _backupServiceMock = new Mock<IBackupService>();
        _pathResolverMock = new Mock<ICanonicalPathResolver>();

        _service = new StartupRecoveryService(
            _journalMock.Object,
            _repositoryMock.Object,
            _fileSystemMock.Object,
            _registryMock.Object,
            _backupServiceMock.Object,
            _pathResolverMock.Object,
            NullLogger<StartupRecoveryService>.Instance);
    }

    [Fact]
    public async Task Reconcile_NoInterruptedSessions_DoesNothing()
    {
        _journalMock.Setup(j => j.GetIncompleteTransactionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionJournalEntry>());

        var result = await _service.ReconcileIncompleteTransactionsAsync();

        result.Should().BeFalse();
        _journalMock.Verify(j => j.RecordStateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TransactionType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_InterruptedCleanup_ConfirmedCompletion()
    {
        var entry = new TransactionJournalEntry { SessionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), TransactionType = TransactionType.Cleanup, State = "Executing" };
        var item = new CleanupPlanItem { Id = entry.ItemId, ArtifactType = ArtifactType.File, Path = "C:\\test.txt" };

        _journalMock.Setup(j => j.GetIncompleteTransactionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { entry });
        _repositoryMock.Setup(r => r.GetCleanupItemAsync(entry.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _fileSystemMock.Setup(f => f.FileExists("C:\\test.txt")).Returns(false); // Target absent

        var result = await _service.ReconcileIncompleteTransactionsAsync();

        result.Should().BeTrue();
        _journalMock.Verify(j => j.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Cleanup, CleanupItemExecutionState.Succeeded.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_InterruptedCleanup_ConfirmedIncomplete()
    {
        var entry = new TransactionJournalEntry { SessionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), TransactionType = TransactionType.Cleanup, State = "Executing" };
        var item = new CleanupPlanItem { Id = entry.ItemId, ArtifactType = ArtifactType.File, Path = "C:\\test.txt" };

        _journalMock.Setup(j => j.GetIncompleteTransactionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { entry });
        _repositoryMock.Setup(r => r.GetCleanupItemAsync(entry.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _fileSystemMock.Setup(f => f.FileExists("C:\\test.txt")).Returns(true); // Target present

        var result = await _service.ReconcileIncompleteTransactionsAsync();

        result.Should().BeTrue();
        _journalMock.Verify(j => j.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Cleanup, CleanupItemExecutionState.Failed.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_InterruptedRecovery_MissingTarget()
    {
        var entry = new TransactionJournalEntry { SessionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), TransactionType = TransactionType.Recovery, State = "Restoring" };
        var backup = new Backup { Id = entry.ItemId, ArtifactType = ArtifactType.File, OriginalPath = "C:\\test.txt" };

        _journalMock.Setup(j => j.GetIncompleteTransactionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { entry });
        _backupServiceMock.Setup(b => b.GetBackupAsync(entry.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(backup);
        _fileSystemMock.Setup(f => f.FileExists("C:\\test.txt")).Returns(false); // Target absent

        var result = await _service.ReconcileIncompleteTransactionsAsync();

        result.Should().BeTrue();
        _journalMock.Verify(j => j.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Recovery, RecoveryItemExecutionState.Failed.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconcile_InterruptedRecovery_ConfirmedCompletion()
    {
        var entry = new TransactionJournalEntry { SessionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), TransactionType = TransactionType.Recovery, State = "Restoring" };
        var backup = new Backup { Id = entry.ItemId, ArtifactType = ArtifactType.File, OriginalPath = "C:\\test.txt" };

        _journalMock.Setup(j => j.GetIncompleteTransactionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { entry });
        _backupServiceMock.Setup(b => b.GetBackupAsync(entry.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(backup);
        _fileSystemMock.Setup(f => f.FileExists("C:\\test.txt")).Returns(true); // Target present

        var result = await _service.ReconcileIncompleteTransactionsAsync();

        result.Should().BeTrue();
        _journalMock.Verify(j => j.RecordStateAsync(entry.SessionId, entry.ItemId, TransactionType.Recovery, RecoveryItemExecutionState.Recovered.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
