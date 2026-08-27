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
    private readonly Mock<IErrorBoundaryService> _mockError;

    public ApplicationDetailsViewModelTests()
    {
        _mockUninstall = new Mock<IUninstallService>();
        _mockAnalysis = new Mock<IResidualAnalysisService>();
        _mockRepo = new Mock<IApplicationRepository>();
        _mockError = new Mock<IErrorBoundaryService>();
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

        var vm = new ApplicationDetailsViewModel(_mockUninstall.Object, _mockAnalysis.Object, _mockRepo.Object, new Mock<INavigationService>().Object, ServiceProviderMock.Create(), _mockError.Object);
        vm.LoadApplication(appVm);
        
        await vm.UninstallCommand.ExecuteAsync(null);

        Assert.Equal(UIState.Success, vm.State);
    }


    [Fact]
    public async Task AnalyzeResidualsAsync_Success_SetsStateToSuccess()
    {
        var appId = Guid.NewGuid();
        var appEntity = new Application { Id = appId, Name = "Test" };
        var appVm = new ApplicationViewModel(appEntity);
        
        _mockRepo.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appEntity);
            
        _mockAnalysis.Setup(a => a.RunAnalysisAsync(It.IsAny<Uninstaller.Domain.Entities.UninstallSession>(), appEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Uninstaller.Domain.Entities.ResidualAnalysisSession { Id = Guid.NewGuid(), Plan = new Uninstaller.Domain.Entities.CleanupPlan() });

        var vm = new ApplicationDetailsViewModel(_mockUninstall.Object, _mockAnalysis.Object, _mockRepo.Object, new Mock<INavigationService>().Object, ServiceProviderMock.Create(), _mockError.Object);
        vm.LoadApplication(appVm);
        
        await vm.AnalyzeResidualsCommand.ExecuteAsync(null);

        Assert.Equal(UIState.Success, vm.State);
    }
}
