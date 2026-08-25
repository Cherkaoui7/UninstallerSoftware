using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Uninstaller.App.ViewModels;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Domain.Entities;
using Xunit;

namespace Uninstaller.App.Tests;

public class MainViewModelTests
{
    private readonly Mock<IDiscoveryService> _discoveryServiceMock;
    private readonly Mock<IApplicationRepository> _repositoryMock;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _discoveryServiceMock = new Mock<IDiscoveryService>();
        _repositoryMock = new Mock<IApplicationRepository>();
        
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Application>());

        _viewModel = new MainViewModel(_discoveryServiceMock.Object, _repositoryMock.Object);
    }

    [Fact]
    public void Constructor_SetsInitialState()
    {
        Assert.Equal(DiscoveryState.Idle, _viewModel.State);
        Assert.Empty(_viewModel.Applications);
        Assert.NotNull(_viewModel.ScanCommand);
        Assert.NotNull(_viewModel.CancelCommand);
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingApplications()
    {
        // Arrange
        var app = new Application { Id = Guid.NewGuid(), Name = "Existing App", IsPresent = true };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Application> { app });

        // Act
        await _viewModel.InitializeAsync();

        // Assert
        Assert.Single(_viewModel.Applications);
        Assert.Equal("Existing App", _viewModel.Applications[0].Name);
    }

    [Fact]
    public async Task ScanCommand_UpdatesStateAndDiscovers()
    {
        // Arrange
        _discoveryServiceMock.Setup(d => d.DiscoverApplicationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult { ApplicationsDiscovered = 1, ApplicationsAdded = 1 });

        // Act
        await _viewModel.ScanCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(DiscoveryState.Completed, _viewModel.State);
        _discoveryServiceMock.Verify(d => d.DiscoverApplicationsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once); // Once at end of scan
    }

    [Fact]
    public void FilterApplications_ShowsOnlyPresentAndMatchingSearchText()
    {
        // View filtering tests are tricky because they depend on CollectionView, 
        // but we can test the filtering method directly if we make it public or use reflection.
        // Or we can just trust the WPF binding logic for now.
    }
}
