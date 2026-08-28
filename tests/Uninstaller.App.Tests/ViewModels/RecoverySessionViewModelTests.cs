using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.App.Enums;
using Xunit;
using AppEntity = Uninstaller.Domain.Entities.Application;

namespace Uninstaller.App.Tests.ViewModels;

public class RecoverySessionViewModelTests
{
    private readonly Mock<IRecoveryTransactionEngine> _transactionEngineMock;
    private readonly Mock<IObservableRecoveryItemExecutionTracker> _trackerMock;
    private readonly Mock<INavigationService> _navigationServiceMock;
    private readonly Mock<IErrorBoundaryService> _errorBoundaryMock;
    private readonly AppEntity _application;
    private readonly UninstallSession _session;

    public RecoverySessionViewModelTests()
    {
        _transactionEngineMock = new Mock<IRecoveryTransactionEngine>();
        _trackerMock = new Mock<IObservableRecoveryItemExecutionTracker>();
        _navigationServiceMock = new Mock<INavigationService>();
        _errorBoundaryMock = new Mock<IErrorBoundaryService>();
        
        _application = new AppEntity { Id = Guid.NewGuid(), Name = "Test App" };
        _session = new UninstallSession { Id = Guid.NewGuid() };
    }

    [Fact]
    public async Task StartExecutionAsync_ShouldCallEngineWithExactItems()
    {
        var backups = new List<Backup>
        {
            new Backup { Id = Guid.NewGuid(), ArtifactType = ArtifactType.File, OriginalPath = @"C:\test.txt" }
        };

        var viewModel = new RecoverySessionViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        _transactionEngineMock
            .Setup(e => e.ExecuteAsync(It.IsAny<RecoverySession>(), _application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoverySessionResult
            {
                Status = RecoverySessionStatus.Completed,
                Results = new List<RecoveryResult>
                {
                    new RecoveryResult { Outcome = RecoveryOutcome.Recovered }
                }
            });

        await viewModel.StartExecutionAsync();

        _transactionEngineMock.Verify(e => e.ExecuteAsync(
            It.Is<RecoverySession>(s => s.Items.Count == 1 && s.Items[0].BackupArtifactId == backups[0].Id),
            _application,
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(UIState.Success, viewModel.State);
    }

    [Fact]
    public async Task StartExecutionAsync_WithConflict_ShouldMapCorrectly()
    {
        var backups = new List<Backup>
        {
            new Backup { Id = Guid.NewGuid(), ArtifactType = ArtifactType.File, OriginalPath = @"C:\test.txt" }
        };

        var viewModel = new RecoverySessionViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        _transactionEngineMock
            .Setup(e => e.ExecuteAsync(It.IsAny<RecoverySession>(), _application, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecoverySession s, AppEntity a, CancellationToken ct) => 
            {
                return new RecoverySessionResult
                {
                    Status = RecoverySessionStatus.CompletedWithFailures,
                    TotalItems = 1,
                    Results = new List<RecoveryResult>
                    {
                        new RecoveryResult { RecoveryItemId = s.Items[0].Id, Outcome = RecoveryOutcome.RecoveryConflict }
                    }
                };
            });

        await viewModel.StartExecutionAsync();

        Assert.Equal(1, viewModel.ConflictCount);
        Assert.Equal(RecoveryItemExecutionState.Conflict, viewModel.Items[0].State);
        Assert.Equal("The original location already contains data, so this item was not restored.", viewModel.Items[0].FailureReason);
        Assert.Equal(UIState.Warning, viewModel.State);
    }

    [Fact]
    public async Task StartExecutionAsync_WithCancellation_ShouldMapCorrectly()
    {
        var backups = new List<Backup>
        {
            new Backup { Id = Guid.NewGuid(), ArtifactType = ArtifactType.File, OriginalPath = @"C:\test.txt" }
        };

        var viewModel = new RecoverySessionViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        var tcs = new TaskCompletionSource<RecoverySessionResult>();

        _transactionEngineMock
            .Setup(e => e.ExecuteAsync(It.IsAny<RecoverySession>(), _application, It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // Trigger execution, state goes to Working
        var execTask = viewModel.StartExecutionAsync();
        
        // Assert we can cancel
        Assert.True(viewModel.CanCancel);
        viewModel.CancelCommand.Execute(null);

        // Resolve mock task
        tcs.SetResult(new RecoverySessionResult
        {
            Status = RecoverySessionStatus.Cancelled,
            TotalItems = 1,
            Results = new List<RecoveryResult>()
        });

        await execTask;

        Assert.Equal(UIState.Cancelled, viewModel.State);
    }
}
