using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;
using AppEntity = Uninstaller.Domain.Entities.Application;

namespace Uninstaller.App.Tests.ViewModels;

public class RecoveryViewModelTests
{
    private readonly Mock<INavigationService> _navigationServiceMock;
    private readonly Mock<IErrorBoundaryService> _errorBoundaryMock;
    private readonly Mock<IRecoveryTransactionEngine> _transactionEngineMock;
    private readonly Mock<IObservableRecoveryItemExecutionTracker> _trackerMock;
    private readonly AppEntity _application;
    private readonly UninstallSession _session;

    public RecoveryViewModelTests()
    {
        _navigationServiceMock = new Mock<INavigationService>();
        _errorBoundaryMock = new Mock<IErrorBoundaryService>();
        _transactionEngineMock = new Mock<IRecoveryTransactionEngine>();
        _trackerMock = new Mock<IObservableRecoveryItemExecutionTracker>();
        
        _application = new AppEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test App",
            Version = "1.0.0"
        };
        
        _session = new UninstallSession
        {
            Id = Guid.NewGuid(),
            ApplicationId = _application.Id,
            Status = UninstallSessionStatus.Completed
        };
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties_WhenValidArgumentsProvided()
    {
        var backups = new List<Backup>();
        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        Assert.Equal("Test App", viewModel.ApplicationName);
        Assert.Equal("1.0.0", viewModel.ApplicationVersion);
        Assert.Equal(_session.Id, viewModel.CleanupSessionId);
        Assert.Empty(viewModel.Items);
        Assert.Equal(0, viewModel.TotalItems);
    }

    [Fact]
    public void VerifiedBackups_ShouldBeInitiallySelectable()
    {
        var backups = new List<Backup>
        {
            new Backup { VerificationStatus = BackupVerificationStatus.Verified, ArtifactType = ArtifactType.File }
        };

        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        Assert.True(viewModel.Items[0].IsRecoverable);
        Assert.True(viewModel.Items[0].IsSelected);
    }

    [Fact]
    public void UnverifiedBackup_ShouldBeBlocked()
    {
        var backups = new List<Backup>
        {
            new Backup { VerificationStatus = BackupVerificationStatus.Unverified, ArtifactType = ArtifactType.File }
        };

        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        Assert.False(viewModel.Items[0].IsRecoverable);
        Assert.False(viewModel.Items[0].IsSelected);
    }

    [Fact]
    public void UnsupportedArtifact_ShouldBeBlocked()
    {
        var backups = new List<Backup>
        {
            new Backup { VerificationStatus = BackupVerificationStatus.Verified, ArtifactType = ArtifactType.Other }
        };

        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        Assert.False(viewModel.Items[0].IsRecoverable);
        Assert.False(viewModel.Items[0].IsSelected);
    }

    [Fact]
    public void ExistingPersistedConflict_ShouldBeBlocked()
    {
        var backups = new List<Backup>
        {
            new Backup { VerificationStatus = BackupVerificationStatus.Verified, ArtifactType = ArtifactType.File, FailureReason = "RecoveryConflict" }
        };

        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        Assert.False(viewModel.Items[0].IsRecoverable);
        Assert.False(viewModel.Items[0].IsSelected);
    }

    [Fact]
    public void UserCanDeselectRecoverableItem()
    {
        var backups = new List<Backup>
        {
            new Backup { VerificationStatus = BackupVerificationStatus.Verified, ArtifactType = ArtifactType.File }
        };

        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);
        
        Assert.True(viewModel.Items[0].IsSelected);
        
        viewModel.Items[0].IsSelected = false;
        
        Assert.False(viewModel.Items[0].IsSelected);
        Assert.Equal(0, viewModel.SelectedItemsCount);
    }

    [Fact]
    public void EmptyRecoverySet_ShouldDisableConfirm()
    {
        var backups = new List<Backup>();
        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);

        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void Confirm_ShouldNavigateToRecoverySession()
    {
        var backups = new List<Backup>
        {
            new Backup { VerificationStatus = BackupVerificationStatus.Verified, ArtifactType = ArtifactType.File }
        };

        var viewModel = new RecoveryViewModel(_application, _session, backups, _transactionEngineMock.Object, _trackerMock.Object, _navigationServiceMock.Object, _errorBoundaryMock.Object);
        
        Assert.True(viewModel.CanConfirm);
        
        viewModel.ConfirmCommand.Execute(null);

        _navigationServiceMock.Verify(n => n.NavigateTo(It.IsAny<RecoverySessionViewModel>()), Times.Once);
    }
}
