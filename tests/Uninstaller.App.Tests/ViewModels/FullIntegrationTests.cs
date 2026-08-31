using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using CommunityToolkit.Mvvm.ComponentModel;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.App.ViewModels;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Xunit;

namespace Uninstaller.App.Tests.ViewModels;

public class FullIntegrationTests
{
    [Fact]
    public async Task RunFullIntegrationTest()
    {
        var services = new ServiceCollection();
        
        var mockAppRepo = new Mock<IApplicationRepository>();
        var mockSessionRepo = new Mock<IUninstallSessionRepository>();
        
        var appId = Guid.NewGuid();
        var app = new Application { Id = appId, Name = "Telegram" };
        var session = new UninstallSession { Id = Guid.NewGuid(), ApplicationId = appId, Status = UninstallSessionStatus.Completed, CreatedAt = DateTime.UtcNow };
        
        mockAppRepo.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        mockSessionRepo.Setup(r => r.GetLatestByApplicationIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        services.AddSingleton<IApplicationRepository>(mockAppRepo.Object);
        services.AddSingleton<IUninstallSessionRepository>(mockSessionRepo.Object);
        
        services.AddSingleton<IResidualAnalysisService, ResidualAnalysisService>();
        services.AddSingleton<ICleanupPlanGenerator, CleanupPlanGenerator>();
        services.AddSingleton<IEvidenceEngine, EvidenceEngine>();
        
        var mockScanner = new Mock<IResidualScanner>();
        mockScanner.Setup(s => s.ScanAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ResidualArtifactCandidate>());
            
        services.AddSingleton<IEnumerable<IResidualScanner>>(new List<IResidualScanner> { mockScanner.Object });
        
        var mockLogger = new Mock<ILogger<ResidualAnalysisService>>();
        services.AddSingleton<ILogger<ResidualAnalysisService>>(mockLogger.Object);
        
        var mockLoggerGenerator = new Mock<ILogger<CleanupPlanGenerator>>();
        services.AddSingleton<ILogger<CleanupPlanGenerator>>(mockLoggerGenerator.Object);
        
        var mockLoggerEvidence = new Mock<ILogger<EvidenceEngine>>();
        services.AddSingleton<ILogger<EvidenceEngine>>(mockLoggerEvidence.Object);
        
        var mockLoggerApp = new Mock<ILogger<ApplicationDetailsViewModel>>();
        services.AddSingleton<ILogger<ApplicationDetailsViewModel>>(mockLoggerApp.Object);
        
        services.AddSingleton(new Mock<IUninstallService>().Object);
        
        var mockNavigation = new Mock<INavigationService>();
        ObservableObject? navObj = null;
        mockNavigation.Setup(n => n.NavigateTo(It.IsAny<ObservableObject>()))
            .Callback<ObservableObject>(o => navObj = o);
            
        services.AddSingleton<INavigationService>(mockNavigation.Object);
        services.AddSingleton(new Mock<IErrorBoundaryService>().Object);
        
        var provider = services.BuildServiceProvider();
        
        var vm = new ApplicationDetailsViewModel(
            provider.GetRequiredService<IUninstallService>(),
            provider.GetRequiredService<IResidualAnalysisService>(),
            provider.GetRequiredService<IApplicationRepository>(),
            provider.GetRequiredService<IUninstallSessionRepository>(),
            provider.GetRequiredService<INavigationService>(),
            provider,
            provider.GetRequiredService<IErrorBoundaryService>()
        );
        
        vm.LoadApplication(new ApplicationViewModel(app));
        
        await vm.AnalyzeResidualsCommand.ExecuteAsync(null);
        
        Assert.Equal(Uninstaller.App.Enums.UIState.Success, vm.State);
        Assert.NotNull(navObj);
        Assert.IsType<CleanupPlanViewModel>(navObj);
    }
}
