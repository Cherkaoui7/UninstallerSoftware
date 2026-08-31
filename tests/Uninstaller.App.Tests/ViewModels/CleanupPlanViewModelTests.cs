using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;
using Uninstaller.App.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Uninstaller.App.Tests.ViewModels;

public class CleanupPlanViewModelTests
{
    private readonly Mock<INavigationService> _mockNavigation;
    private readonly Mock<ICleanupViewModelFactory> _mockCleanupFactory;
    private readonly Mock<IErrorBoundaryService> _mockErrorBoundary;
    private readonly Application _appEntity;
    private readonly CleanupPlan _plan;

    public CleanupPlanViewModelTests()
    {
        _mockNavigation = new Mock<INavigationService>();
        _mockCleanupFactory = new Mock<ICleanupViewModelFactory>();
        _mockErrorBoundary = new Mock<IErrorBoundaryService>();
        
        _appEntity = new Application { Id = Guid.NewGuid(), Name = "Test App" };
        _plan = new CleanupPlan
        {
            ApplicationId = _appEntity.Id,
            Items = new List<CleanupPlanItem>
            {
                new CleanupPlanItem { Id = Guid.NewGuid(), Path = "C:\\test.txt", Classification = ArtifactClassification.UserData, Recommended = true },
                new CleanupPlanItem { Id = Guid.NewGuid(), Path = "HKLM\\test", Classification = ArtifactClassification.SharedDependency, IsProtected = true, Recommended = false }
            }
        };
    }

    private CleanupPlanViewModel CreateViewModel()
    {
        return new CleanupPlanViewModel(
            _plan,
            _appEntity,
            _mockNavigation.Object,
            _mockCleanupFactory.Object,
            _mockErrorBoundary.Object);
    }

    [Fact]
    public void Constructor_InitializesStateCorrectly()
    {
        // Setup & Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal(UIState.Ready, vm.State);
        Assert.Equal(2, vm.Items.Count);
        
        
        
        Assert.True(vm.Items[0].IsSelected);
        Assert.False(vm.Items[1].IsSelected);
    }

    [Fact]
    public void ExecuteCleanup_NavigatesToExecutionViewWithSelectedItems()
    {
        // Setup
        var vm = CreateViewModel();
        
        // Select only the first one
        vm.Items[0].IsSelected = true;
        vm.Items[1].IsSelected = false;

        // Act
        vm.ExecuteCleanupCommand.Execute(null);

        // Assert
        _mockNavigation.Verify(x => x.NavigateTo(It.IsAny<CleanupExecutionViewModel>()), Times.Once);
    }
}
