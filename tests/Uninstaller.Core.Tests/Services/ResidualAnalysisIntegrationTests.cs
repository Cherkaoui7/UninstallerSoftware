using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class ResidualAnalysisIntegrationTests
{
    private ServiceProvider _serviceProvider;

    public ResidualAnalysisIntegrationTests()
    {
        var services = new ServiceCollection();

        // 1. Mock Repositories
        var appRepoMock = new Mock<IApplicationRepository>();
        appRepoMock.Setup(r => r.SaveAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sessionRepoMock = new Mock<IUninstallSessionRepository>();
        sessionRepoMock.Setup(r => r.SaveAsync(It.IsAny<UninstallSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
            
        // We will configure GetLatestByApplicationIdAsync in the test method itself
        services.AddSingleton(appRepoMock.Object);
        services.AddSingleton(sessionRepoMock.Object);
        // Also register the mocks so we can configure them in the tests
        services.AddSingleton(appRepoMock);
        services.AddSingleton(sessionRepoMock);

        // 2. Core Services
        services.AddScoped<IApplicationDeduplicator, ApplicationDeduplicator>();
        services.AddScoped<ICleanupPlanGenerator, CleanupPlanGenerator>();
        services.AddScoped<IEvidenceEngine, EvidenceEngine>();
        services.AddScoped<IResidualAnalysisService, ResidualAnalysisService>();

        // 3. Mock Scanners
        services.AddSingleton<IResidualScanner, MockScanner>();

        // 4. Logging
        var mockLoggerAnalysis = new Mock<ILogger<ResidualAnalysisService>>();
        var mockLoggerGenerator = new Mock<ILogger<CleanupPlanGenerator>>();
        var mockLoggerEvidence = new Mock<ILogger<EvidenceEngine>>();
        
        services.AddSingleton(mockLoggerAnalysis.Object);
        services.AddSingleton(mockLoggerGenerator.Object);
        services.AddSingleton(mockLoggerEvidence.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Telegram_CompletedUninstall_ShouldGenerateCleanupPlan()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var sessionRepoMock = sp.GetRequiredService<Mock<IUninstallSessionRepository>>();
        var analysisService = sp.GetRequiredService<IResidualAnalysisService>();

        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = "Telegram Desktop",
            Publisher = "Telegram FZ-LLC",
            InstallLocation = @"C:\Users\test\AppData\Roaming\Telegram Desktop",
            IsPresent = true
        };

        var session = new UninstallSession
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Status = UninstallSessionStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        sessionRepoMock.Setup(r => r.GetLatestByApplicationIdAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var latestSession = await sessionRepoMock.Object.GetLatestByApplicationIdAsync(app.Id, CancellationToken.None);
        Assert.NotNull(latestSession);
        Assert.Equal(UninstallSessionStatus.Completed, latestSession.Status);

        var analysisSession = await analysisService.RunAnalysisAsync(latestSession, app, CancellationToken.None);

        // Assert
        Assert.NotNull(analysisSession.Plan);
        Assert.Equal(ResidualAnalysisStatus.Completed, analysisSession.Status);
    }

    private class MockScanner : IResidualScanner
    {
        public string Name => "Mock Scanner";
        public Task<IReadOnlyList<ResidualArtifactCandidate>> ScanAsync(Application application, CancellationToken cancellationToken)
        {
            var list = new List<ResidualArtifactCandidate>();
            return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(list);
        }
    }
}
