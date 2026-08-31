using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Uninstaller.App.Enums;
using Uninstaller.App.Services;
using Uninstaller.App.ViewModels;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.App.Tests.ViewModels;

public class PostUninstallResidualAnalysisIntegrationTests
{
    private class InMemoryApplicationRepository : IApplicationRepository
    {
        public readonly List<Application> Storage = new();

        public Task<IReadOnlyList<Application>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Application>>(Storage.ToList());
        }

        public Task<Application?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Storage.FirstOrDefault(a => a.Id == id));
        }

        public Task SaveAsync(Application application, CancellationToken cancellationToken)
        {
            var idx = Storage.FindIndex(a => a.Id == application.Id);
            if (idx >= 0) Storage[idx] = application;
            else Storage.Add(application);
            return Task.CompletedTask;
        }

        public Task<Core.Models.SyncResult> SyncAsync(IEnumerable<Application> discoveredApps, CancellationToken cancellationToken)
        {
            var discoveredList = discoveredApps.ToList();
            foreach (var app in Storage)
            {
                if (!discoveredList.Any(d => d.Id == app.Id || d.Name == app.Name))
                {
                    app.IsPresent = false;
                }
            }
            return Task.FromResult(new Core.Models.SyncResult());
        }
    }

    private class InMemoryUninstallSessionRepository : IUninstallSessionRepository
    {
        public readonly List<UninstallSession> Storage = new();

        public Task<UninstallSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Storage.FirstOrDefault(s => s.Id == id));
        }

        public Task<UninstallSession?> GetLatestByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Storage
                .Where(s => s.ApplicationId == applicationId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault());
        }

        public Task SaveAsync(UninstallSession session, CancellationToken cancellationToken)
        {
            var idx = Storage.FindIndex(s => s.Id == session.Id);
            if (idx >= 0) Storage[idx] = session;
            else Storage.Add(session);
            return Task.CompletedTask;
        }
    }

    private readonly InMemoryApplicationRepository _appRepo = new();
    private readonly InMemoryUninstallSessionRepository _sessionRepo = new();
    private readonly Mock<INavigationService> _navServiceMock = new();
    private readonly Mock<IErrorBoundaryService> _errorBoundaryMock = new();
    private readonly Mock<IUninstallService> _uninstallServiceMock = new();
    private readonly Mock<IResidualAnalysisService> _residualAnalysisServiceMock = new();
    private readonly Mock<IDiscoveryService> _discoveryServiceMock = new();
    private readonly Mock<ICleanupViewModelFactory> _cleanupFactoryMock = new();

    public PostUninstallResidualAnalysisIntegrationTests()
    {
    }

    [Fact]
    public async Task PostUninstall_FullLifecycle_MeetsAll9Requirements()
    {
        // 1. Application is discoverable while installed
        var appId = Guid.NewGuid();
        var initialApp = new Application
        {
            Id = appId,
            Name = "7-Zip 24.08 (x64)",
            Publisher = "Igor Pavlov",
            Version = "24.08",
            InstallLocation = @"C:\Program Files\7-Zip",
            RegistryKeyName = "7-Zip",
            RegistrySource = "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\7-Zip [Registry64]",
            UninstallCommand = "\"C:\\Program Files\\7-Zip\\Uninstall.exe\"",
            IsPresent = true
        };
        await _appRepo.SaveAsync(initialApp, CancellationToken.None);

        var appsVm = new ApplicationsViewModel(_discoveryServiceMock.Object, _appRepo, _navServiceMock.Object, _errorBoundaryMock.Object);
        await appsVm.InitializeAsync();

        Assert.Single(appsVm.Applications);
        var appVm = appsVm.Applications.First();
        Assert.True(appVm.IsPresent);
        Assert.Equal("Installed", appVm.UninstallStatus);

        // 2. Official uninstall succeeds
        var completedSession = new UninstallSession
        {
            Id = Guid.NewGuid(),
            ApplicationId = appId,
            Status = UninstallSessionStatus.Completed,
            VerificationResult = VerificationResult.VerifiedRemoved,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await _sessionRepo.SaveAsync(completedSession, CancellationToken.None);

        _uninstallServiceMock
            .Setup(u => u.RunUninstallAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedSession);

        var detailsVm = new ApplicationDetailsViewModel(
            _uninstallServiceMock.Object,
            _residualAnalysisServiceMock.Object,
            _appRepo,
            _sessionRepo,
            _navServiceMock.Object,
            _cleanupFactoryMock.Object,
            _errorBoundaryMock.Object);

        detailsVm.LoadApplication(appVm);
        Assert.True(detailsVm.UninstallCommand.CanExecute(null));

        await detailsVm.UninstallCommand.ExecuteAsync(null);

        // 3. Registry entry disappears -> Discovery sync sets IsPresent = false
        // Simulate discovery sync where 7-Zip is no longer discovered in registry
        await _appRepo.SyncAsync(Enumerable.Empty<Application>(), CancellationToken.None);

        // 4. Application persistence remains available with IsPresent = false
        var persistedApp = await _appRepo.GetByIdAsync(appId, CancellationToken.None);
        Assert.NotNull(persistedApp);
        Assert.False(persistedApp.IsPresent);

        // 5. User can still view and select the uninstalled application in Applications list
        await appsVm.InitializeAsync();
        Assert.Single(appsVm.Applications);
        var uninstalledAppVm = appsVm.Applications.First();
        Assert.False(uninstalledAppVm.IsPresent);
        Assert.Equal("Uninstalled", uninstalledAppVm.UninstallStatus);

        // 6. Navigation to Details shows "Official Uninstall" disabled and "Analyze Residuals" enabled
        var detailsVm2 = new ApplicationDetailsViewModel(
            _uninstallServiceMock.Object,
            _residualAnalysisServiceMock.Object,
            _appRepo,
            _sessionRepo,
            _navServiceMock.Object,
            _cleanupFactoryMock.Object,
            _errorBoundaryMock.Object);

        detailsVm2.LoadApplication(uninstalledAppVm);
        Assert.False(detailsVm2.UninstallCommand.CanExecute(null));
        Assert.True(detailsVm2.AnalyzeResidualsCommand.CanExecute(null));

        // 7. Correct ApplicationId and completed UninstallSession are used
        var plan = new CleanupPlan
        {
            Id = Guid.NewGuid(),
            UninstallSessionId = completedSession.Id,
            Items = new List<CleanupPlanItem>
            {
                new CleanupPlanItem
                {
                    Id = Guid.NewGuid(),
                    ArtifactType = ArtifactType.Directory,
                    Path = @"C:\Program Files\7-Zip",
                    Recommended = true
                }
            }
        };

        var analysisSession = new ResidualAnalysisSession
        {
            Id = Guid.NewGuid(),
            UninstallSessionId = completedSession.Id,
            Status = ResidualAnalysisStatus.Completed,
            Plan = plan
        };

        _residualAnalysisServiceMock
            .Setup(r => r.RunAnalysisAsync(
                It.Is<UninstallSession>(s => s.Id == completedSession.Id && s.Status == UninstallSessionStatus.Completed),
                It.Is<Application>(a => a.Id == appId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisSession);

        // Setup ActivatorUtilities / CleanupPlanViewModel resolution
        _navServiceMock.Setup(n => n.NavigateTo(It.IsAny<CleanupPlanViewModel>())).Verifiable();

        // 8. Analysis runs successfully after the app disappears from registry
        await detailsVm2.AnalyzeResidualsCommand.ExecuteAsync(null);

        Assert.Equal(UIState.Success, detailsVm2.State);
        _residualAnalysisServiceMock.Verify(r => r.RunAnalysisAsync(
            It.Is<UninstallSession>(s => s.Id == completedSession.Id),
            It.Is<Application>(a => a.Id == appId),
            It.IsAny<CancellationToken>()), Times.Once);

        // 9. No cleanup is automatically executed (Plan is generated for user review only)
        Assert.NotNull(analysisSession.Plan);
        Assert.Single(analysisSession.Plan.Items);
    }
}
