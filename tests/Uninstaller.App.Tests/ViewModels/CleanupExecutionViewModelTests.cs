using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;
using Uninstaller.App.Enums;

namespace Uninstaller.App.Tests.ViewModels;

public class CleanupExecutionViewModelTests
{
    private readonly Mock<ICleanupTransactionEngine> _mockEngine;
    private readonly ObservableItemExecutionTracker _tracker;
    private readonly Mock<INavigationService> _mockNavigation;
    private readonly Mock<IErrorBoundaryService> _mockErrorBoundary;
    private readonly CleanupPlan _plan;
    private readonly List<Guid> _selectedIds;
    private readonly Application _appEntity;

    public CleanupExecutionViewModelTests()
    {
        _mockEngine = new Mock<ICleanupTransactionEngine>();
        _tracker = new ObservableItemExecutionTracker();
        _mockNavigation = new Mock<INavigationService>();
        _mockErrorBoundary = new Mock<IErrorBoundaryService>();
        var appId = Guid.NewGuid();
        _appEntity = new Application { Id = appId, Name = "Test App" };
        
        var item1 = new CleanupPlanItem { Id = Guid.NewGuid(), Path = "C:\\test1.txt" };
        var item2 = new CleanupPlanItem { Id = Guid.NewGuid(), Path = "HKLM\\Software\\Test" };
        var item3 = new CleanupPlanItem { Id = Guid.NewGuid(), Path = "C:\\test3.txt" };
        
        _plan = new CleanupPlan
        {
            ApplicationId = appId,
            Items = new List<CleanupPlanItem> { item1, item2, item3 }
        };
        
        _selectedIds = new List<Guid> { item1.Id, item2.Id };
    }

    private CleanupExecutionViewModel CreateViewModel()
    {
        return new CleanupExecutionViewModel(
            _plan, _appEntity, _selectedIds, _mockEngine.Object, _tracker, _mockNavigation.Object, _mockErrorBoundary.Object);
    }

    [Fact]
    public void Constructor_InitializesItemsCorrectly()
    {
        var vm = CreateViewModel();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal("Test App", vm.ApplicationName);
        Assert.All(vm.Items, i => Assert.Equal(CleanupItemExecutionState.Pending, i.State));
    }

    [Fact]
    public async Task StartExecutionAsync_ExecutesEngineAndUpdatesResult()
    {
        var vm = CreateViewModel();
        var sessionResult = new CleanupSessionResult
        {
            Status = CleanupSessionStatus.Completed,
            ProcessedCount = 2,
            SuccessCount = 2,
            Results = new List<CleanupExecutionResult>
            {
                new CleanupExecutionResult { ItemId = _selectedIds[0], Outcome = CleanupOutcome.DeletedAndVerified },
                new CleanupExecutionResult { ItemId = _selectedIds[1], Outcome = CleanupOutcome.DeletedAndVerified }
            }
        };

        _mockEngine.Setup(x => x.ExecuteAsync(_plan, It.IsAny<Application>(), _selectedIds, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionResult);

        await vm.StartExecutionAsync();

        Assert.Equal(UIState.Success, vm.State);
        Assert.Equal(2, vm.SuccessCount);
        Assert.Equal(2, vm.CompletedCount);
        Assert.Equal(CleanupOutcome.DeletedAndVerified, vm.Items[0].Outcome);
        
        _mockEngine.Verify(x => x.ExecuteAsync(_plan, It.IsAny<Application>(), _selectedIds, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelCommand_CancelsExecutionToken()
    {
        var vm = CreateViewModel();
        var sessionResult = new CleanupSessionResult { Status = CleanupSessionStatus.Cancelled };

        _mockEngine.Setup(x => x.ExecuteAsync(_plan, It.IsAny<Application>(), _selectedIds, It.IsAny<CancellationToken>()))
            .Returns(async (CleanupPlan p, Application a, IEnumerable<Guid> i, CancellationToken ct) => 
            {
                vm.CancelCommand.Execute(null); // Simulate user clicking cancel during execution
                await Task.Delay(50);
                return sessionResult;
            });

        await vm.StartExecutionAsync();

        Assert.Equal(UIState.Cancelled, vm.State);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTracker()
    {
        var vm = CreateViewModel();
        vm.Dispose();
        
        // Changing tracker state should no longer affect items since it's disposed
        _tracker.UpdateStateAsync(_selectedIds[0], CleanupItemExecutionState.Executing);
        
        Assert.Equal(CleanupItemExecutionState.Pending, vm.Items[0].State);
    }
}
