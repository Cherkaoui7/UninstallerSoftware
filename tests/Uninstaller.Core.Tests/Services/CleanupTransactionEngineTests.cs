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

public class CleanupTransactionEngineTests
{
    private readonly Mock<ICleanupPreflightValidator> _preflightValidatorMock;
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<IExecutorResolver> _executorResolverMock;
    private readonly Mock<IItemExecutionTracker> _executionTrackerMock;
    private readonly Mock<ITransactionJournal> _journalMock;
    private readonly Mock<ICleanupExecutor> _executorMock;
    private readonly CleanupTransactionEngine _engine;

    public CleanupTransactionEngineTests()
    {
        _preflightValidatorMock = new Mock<ICleanupPreflightValidator>();
        _backupServiceMock = new Mock<IBackupService>();
        _executorResolverMock = new Mock<IExecutorResolver>();
        _executionTrackerMock = new Mock<IItemExecutionTracker>();
        _journalMock = new Mock<ITransactionJournal>();
        _executorMock = new Mock<ICleanupExecutor>();

        _engine = new CleanupTransactionEngine(
            _preflightValidatorMock.Object,
            _backupServiceMock.Object,
            _executorResolverMock.Object,
            _executionTrackerMock.Object,
            _journalMock.Object,
            NullLogger<CleanupTransactionEngine>.Instance);
    }

    private CleanupPlan CreatePlan(params CleanupPlanItem[] items)
    {
        var plan = new CleanupPlan { ApplicationId = Guid.NewGuid() };
        foreach (var item in items) plan.Items.Add(item);
        return plan;
    }

    private CleanupPlanItem CreateItem(ArtifactType artifactType = ArtifactType.File)
    {
        return new CleanupPlanItem
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Test",
            ArtifactType = artifactType
        };
    }

    private void SetupSuccess(CleanupPlanItem item)
    {
        _preflightValidatorMock
            .Setup(v => v.ValidateAsync(item, It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreflightValidationResult { Outcome = PreflightValidationOutcome.Authorized });

        _backupServiceMock
            .Setup(b => b.BackupArtifactAsync(item, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Backup { VerificationStatus = BackupVerificationStatus.Verified });
            
        _backupServiceMock
            .Setup(b => b.VerifyBackupAsync(It.IsAny<Backup>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupVerificationResult { IsValid = true });

        _executorResolverMock
            .Setup(r => r.Resolve(item.ArtifactType))
            .Returns(_executorMock.Object);

        _executorMock
            .Setup(e => e.ExecuteAsync(It.Is<AuthorizedExecutionContext>(c => c.CleanupPlanItemId == item.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupExecutionResult { Success = true, Outcome = CleanupOutcome.DeletedAndVerified });
    }

    [Fact]
    public async Task ExecuteAsync_AllItemsSucceed_ReturnsCompleted()
    {
        var item1 = CreateItem();
        var item2 = CreateItem();
        var plan = CreatePlan(item1, item2);
        SetupSuccess(item1);
        SetupSuccess(item2);

        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item1.Id, item2.Id });

        result.Status.Should().Be(CleanupSessionStatus.Completed);
        result.ProcessedCount.Should().Be(2);
        result.SuccessCount.Should().Be(2);
        result.FailureCount.Should().Be(0);
        result.Results.Should().HaveCount(2);
        result.Results.All(r => r.Success).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_OnePreflightFailure_ContinuesAndReturnsCompletedWithFailures()
    {
        var item1 = CreateItem();
        var item2 = CreateItem();
        var plan = CreatePlan(item1, item2);
        
        // Item 1 fails preflight
        _preflightValidatorMock.Setup(v => v.ValidateAsync(item1, It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreflightValidationResult { Outcome = PreflightValidationOutcome.ValidationError });
        SetupSuccess(item2);

        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item1.Id, item2.Id });

        result.Status.Should().Be(CleanupSessionStatus.CompletedWithFailures);
        result.SuccessCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.FailureCount.Should().Be(0);
        
        // Ensure executor was NEVER called for item1
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<AuthorizedExecutionContext>(c => c.CleanupPlanItemId == item1.Id), It.IsAny<CancellationToken>()), Times.Never);
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<AuthorizedExecutionContext>(c => c.CleanupPlanItemId == item2.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BackupNotVerified_ExecutorNeverCalled()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        SetupSuccess(item);
        
        _backupServiceMock.Setup(b => b.VerifyBackupAsync(It.IsAny<Backup>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupVerificationResult { IsValid = false });

        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item.Id });

        result.Status.Should().Be(CleanupSessionStatus.CompletedWithFailures);
        result.FailureCount.Should().Be(1);
        
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<AuthorizedExecutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_FinalValidationFails_ExecutorNeverCalled()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        SetupSuccess(item);
        
        // First preflight succeeds, second (final) fails
        _preflightValidatorMock.SetupSequence(v => v.ValidateAsync(item, It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreflightValidationResult { Outcome = PreflightValidationOutcome.Authorized })
            .ReturnsAsync(new PreflightValidationResult { Outcome = PreflightValidationOutcome.ValidationError });

        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item.Id });

        result.Status.Should().Be(CleanupSessionStatus.CompletedWithFailures);
        result.FailureCount.Should().Be(1);
        
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<AuthorizedExecutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MissingExecutor_ReturnsFailureForThatItem()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        SetupSuccess(item);
        
        _executorResolverMock.Setup(r => r.Resolve(item.ArtifactType)).Returns((ICleanupExecutor?)null);

        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item.Id });

        result.Status.Should().Be(CleanupSessionStatus.CompletedWithFailures);
        result.FailureCount.Should().Be(1);
        result.Results.First().FailureReason.Should().Contain("Unsupported artifact type");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationBeforeFirstItem_ReturnsCancelled()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item.Id }, cts.Token);

        result.Status.Should().Be(CleanupSessionStatus.Cancelled);
        result.ProcessedCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        
        _preflightValidatorMock.Verify(v => v.ValidateAsync(It.IsAny<CleanupPlanItem>(), It.IsAny<Application>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_EmptySelection_HandledSafely()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        
        var result = await _engine.ExecuteAsync(plan, new Application(), Array.Empty<Guid>());

        result.Status.Should().Be(CleanupSessionStatus.Completed);
        result.ProcessedCount.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateSelectedIds_HandledSafely()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        SetupSuccess(item);
        
        // Select the same ID twice (the engine filters by Plan.Items so it should only process it once)
        var result = await _engine.ExecuteAsync(plan, new Application(), new[] { item.Id, item.Id });

        result.ProcessedCount.Should().Be(1);
        result.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_PerItemStateTransitions_AreDeterministic()
    {
        var item = CreateItem();
        var plan = CreatePlan(item);
        SetupSuccess(item);
        
        await _engine.ExecuteAsync(plan, new Application(), new[] { item.Id });

        // Assert all states were reached in order
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.Pending), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.Validating), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.PreflightAuthorized), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.BackingUp), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.BackupVerified), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.FinalValidating), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.Executing), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.Verifying), Times.Once);
        _executionTrackerMock.Verify(t => t.UpdateStateAsync(item.Id, CleanupItemExecutionState.Succeeded), Times.Once);
    }
}
