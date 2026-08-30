using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.App.ViewModels;
using Uninstaller.App.Services;
using Uninstaller.App.Enums;
using Xunit;

using Microsoft.Extensions.DependencyInjection;

namespace Uninstaller.App.Tests.ViewModels;

public static class ServiceProviderMock 
{ 
    public static IServiceProvider Create() 
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<Uninstaller.App.Services.INavigationService>().Object);
        services.AddSingleton(new Mock<Uninstaller.Core.Abstractions.ICleanupTransactionEngine>().Object);
        services.AddSingleton(new Mock<Uninstaller.App.Services.IErrorBoundaryService>().Object);
        services.AddSingleton(new Mock<Uninstaller.App.Services.IObservableItemExecutionTracker>().Object);
        return services.BuildServiceProvider();
    } 
}

public class ApplicationDetailsViewModelTests
{
    private readonly Mock<IUninstallService> _mockUninstall;
    private readonly Mock<IResidualAnalysisService> _mockAnalysis;
    private readonly Mock<IApplicationRepository> _mockRepo;
    private readonly Mock<IUninstallSessionRepository> _mockSessionRepo;
    private readonly Mock<IErrorBoundaryService> _mockError;

    public ApplicationDetailsViewModelTests()
    {
        _mockUninstall = new Mock<IUninstallService>();
        _mockAnalysis = new Mock<IResidualAnalysisService>();
        _mockRepo = new Mock<IApplicationRepository>();
        _mockSessionRepo = new Mock<IUninstallSessionRepository>();
        _mockError = new Mock<IErrorBoundaryService>();
    }

    private ApplicationDetailsViewModel CreateViewModel()
    {
        return new ApplicationDetailsViewModel(
            _mockUninstall.Object, 
            _mockAnalysis.Object, 
            _mockRepo.Object, 
            _mockSessionRepo.Object,
            new Mock<INavigationService>().Object, 
            ServiceProviderMock.Create(), 
            _mockError.Object);
    }

    [Fact]
    public void DetailsView_DisplaysSelectedApplication()
    {
        var appVm = new ApplicationViewModel(new Application { Id = Guid.NewGuid(), Name = "Test" });
        var vm = CreateViewModel();

        vm.LoadApplication(appVm);

        Assert.NotNull(vm.Application);
        Assert.Equal("Test", vm.Application.Name);
    }

    [Fact]
    public void DetailsView_SelectedApplication_PreservesId()
    {
        var appId = Guid.NewGuid();
        var appVm = new ApplicationViewModel(new Application { Id = appId, Name = "Test" });
        var vm = CreateViewModel();

        vm.LoadApplication(appVm);

        Assert.NotNull(vm.Application);
        Assert.Equal(appId, vm.Application.Id);
    }

    [Fact]
    public async Task UninstallAsync_Success_SetsStateToSuccess()
    {
        var appId = Guid.NewGuid();
        var appEntity = new Application { Id = appId, Name = "Test" };
        var appVm = new ApplicationViewModel(appEntity);
        
        _mockRepo.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appEntity);
            
        _mockUninstall.Setup(u => u.RunUninstallAsync(appEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UninstallSession { Status = UninstallSessionStatus.Completed });

        var vm = CreateViewModel();
        vm.LoadApplication(appVm);
        
        await vm.UninstallCommand.ExecuteAsync(null);

        Assert.Equal(UIState.Success, vm.State);
    }

    [Theory]
    [InlineData(UninstallSessionStatus.Completed, UIState.Success)]
    [InlineData(UninstallSessionStatus.Created, UIState.Error)]
    [InlineData(UninstallSessionStatus.Failed, UIState.Error)]
    [InlineData(UninstallSessionStatus.Cancelled, UIState.Error)]
    public async Task AnalyzeResidualsAsync_WithSessionStatus_HandlesCorrectly(UninstallSessionStatus status, UIState expectedState)
    {
        var appId = Guid.NewGuid();
        var appEntity = new Application { Id = appId, Name = "Test" };
        var appVm = new ApplicationViewModel(appEntity);
        
        var session = new UninstallSession { Id = Guid.NewGuid(), ApplicationId = appId, Status = status };

        _mockRepo.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appEntity);
            
        _mockSessionRepo.Setup(s => s.GetLatestByApplicationIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
            
        _mockAnalysis.Setup(a => a.RunAnalysisAsync(session, appEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Uninstaller.Domain.Entities.ResidualAnalysisSession { Id = Guid.NewGuid(), Plan = new Uninstaller.Domain.Entities.CleanupPlan() });

        var vm = CreateViewModel();
        vm.LoadApplication(appVm);
        
        await vm.AnalyzeResidualsCommand.ExecuteAsync(null);

        Assert.Equal(expectedState, vm.State);
        
        if (status != UninstallSessionStatus.Completed)
        {
            Assert.Equal("Residual analysis requires a completed uninstall.", vm.ErrorMessage);
            _mockAnalysis.Verify(a => a.RunAnalysisAsync(It.IsAny<UninstallSession>(), It.IsAny<Application>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            _mockAnalysis.Verify(a => a.RunAnalysisAsync(session, appEntity, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task AnalyzeResidualsAsync_NoSession_Rejects()
    {
        var appId = Guid.NewGuid();
        var appEntity = new Application { Id = appId, Name = "Test" };
        
        _mockRepo.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appEntity);
            
        _mockSessionRepo.Setup(s => s.GetLatestByApplicationIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UninstallSession?)null);

        var vm = CreateViewModel();
        vm.LoadApplication(new ApplicationViewModel(appEntity));
        
        await vm.AnalyzeResidualsCommand.ExecuteAsync(null);

        Assert.Equal(UIState.Error, vm.State);
        Assert.Equal("Residual analysis requires a completed uninstall.", vm.ErrorMessage);
        _mockAnalysis.Verify(a => a.RunAnalysisAsync(It.IsAny<UninstallSession>(), It.IsAny<Application>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
