using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class ResidualAnalysisServiceTests
{
    private readonly Mock<IResidualScanner> _scanner1Mock;
    private readonly Mock<IResidualScanner> _scanner2Mock;
    private readonly ResidualAnalysisService _service;

    public ResidualAnalysisServiceTests()
    {
        _scanner1Mock = new Mock<IResidualScanner>();
        _scanner1Mock.SetupGet(s => s.Name).Returns("Scanner1");
        
        _scanner2Mock = new Mock<IResidualScanner>();
        _scanner2Mock.SetupGet(s => s.Name).Returns("Scanner2");

        var scanners = new List<IResidualScanner> { _scanner1Mock.Object, _scanner2Mock.Object };
        
        _service = new ResidualAnalysisService(scanners, NullLogger<ResidualAnalysisService>.Instance);
    }

    [Fact]
    public async Task RunAnalysisAsync_InvalidSessionState_ReturnsFailedSession()
    {
        var uninstallSession = new UninstallSession { Id = Guid.NewGuid(), Status = UninstallSessionStatus.Executing };
        var application = new Application { Id = Guid.NewGuid(), Name = "App" };

        var result = await _service.RunAnalysisAsync(uninstallSession, application);

        Assert.Equal(ResidualAnalysisStatus.Failed, result.Status);
        Assert.Contains("incomplete uninstall session", result.FailureReason);
        Assert.Equal(0, result.ArtifactCount);
        
        _scanner1Mock.Verify(s => s.ScanAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAnalysisAsync_SuccessfulScan_ReturnsCompletedSession()
    {
        var uninstallSession = new UninstallSession { Id = Guid.NewGuid(), Status = UninstallSessionStatus.Completed };
        var application = new Application { Id = Guid.NewGuid(), Name = "App" };

        var artifacts1 = new List<ResidualArtifactCandidate> { 
            new ResidualArtifactCandidate(new Artifact { Path = "C:\\file1" }, new List<Evidence>(), "Test"), 
            new ResidualArtifactCandidate(new Artifact { Path = "C:\\file2" }, new List<Evidence>(), "Test") 
        };
        var artifacts2 = new List<ResidualArtifactCandidate> { 
            new ResidualArtifactCandidate(new Artifact { Path = "HKCU\\key1" }, new List<Evidence>(), "Test") 
        };

        _scanner1Mock.Setup(s => s.ScanAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(artifacts1);
        _scanner2Mock.Setup(s => s.ScanAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(artifacts2);

        var result = await _service.RunAnalysisAsync(uninstallSession, application);

        Assert.Equal(ResidualAnalysisStatus.Completed, result.Status);
        Assert.Equal(3, result.ArtifactCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.NotNull(result.StartedAt);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public async Task RunAnalysisAsync_PartialFailure_ContinuesScanningAndLogsError()
    {
        var uninstallSession = new UninstallSession { Id = Guid.NewGuid(), Status = UninstallSessionStatus.Completed };
        var application = new Application { Id = Guid.NewGuid(), Name = "App" };

        _scanner1Mock.Setup(s => s.ScanAsync(application, It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("Scanner failed"));
        
        var artifacts2 = new List<ResidualArtifactCandidate> { new ResidualArtifactCandidate(new Artifact { Path = "HKCU\\key1" }, new List<Evidence>(), "Test") };
        _scanner2Mock.Setup(s => s.ScanAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(artifacts2);

        var result = await _service.RunAnalysisAsync(uninstallSession, application);

        // Analysis still completes but logs an error count
        Assert.Equal(ResidualAnalysisStatus.Completed, result.Status);
        Assert.Equal(1, result.ArtifactCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Null(result.FailureReason); // Overall process didn't fail
    }

    [Fact]
    public async Task RunAnalysisAsync_Cancellation_ReturnsCancelledSession()
    {
        var uninstallSession = new UninstallSession { Id = Guid.NewGuid(), Status = UninstallSessionStatus.Completed };
        var application = new Application { Id = Guid.NewGuid(), Name = "App" };

        using var cts = new CancellationTokenSource();

        _scanner1Mock.Setup(s => s.ScanAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ResidualArtifactCandidate>());
        
        _scanner2Mock.Setup(s => s.ScanAsync(application, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                cts.Cancel(); // Simulate cancel during scan 2
                await Task.Delay(10, cts.Token); 
                return new List<ResidualArtifactCandidate>();
            });

        var result = await _service.RunAnalysisAsync(uninstallSession, application, cts.Token);

        Assert.Equal(ResidualAnalysisStatus.Cancelled, result.Status);
        Assert.Contains("cancelled", result.FailureReason);
        Assert.NotNull(result.CompletedAt);
    }
}
