using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class UninstallServiceTests
{
    private readonly Mock<IUninstallSessionRepository> _sessionRepoMock;
    private readonly Mock<IApplicationRepository> _appRepoMock;
    private readonly Mock<ICommandParser> _parserMock;
    private readonly Mock<IProcessExecutor> _executorMock;
    private readonly Mock<IDiscoveryService> _discoveryMock;
    private readonly UninstallService _service;

    public UninstallServiceTests()
    {
        _sessionRepoMock = new Mock<IUninstallSessionRepository>();
        _appRepoMock = new Mock<IApplicationRepository>();
        _parserMock = new Mock<ICommandParser>();
        _executorMock = new Mock<IProcessExecutor>();
        _discoveryMock = new Mock<IDiscoveryService>();
        
        _service = new UninstallService(
            _sessionRepoMock.Object,
            _appRepoMock.Object,
            _parserMock.Object,
            _executorMock.Object,
            _discoveryMock.Object,
            NullLogger<UninstallService>.Instance
        );
    }

    [Fact]
    public async Task RunUninstallAsync_VerifiedRemoved_TransitionsToCompleted()
    {
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = "valid.exe" };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "valid.exe" };
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);
        _executorMock.Setup(e => e.ExecuteAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, ProcessId = 123 });
            
        _discoveryMock.Setup(d => d.DiscoverApplicationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult());
            
        // Mock repo returning null or IsPresent = false
        _appRepoMock.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application { Id = app.Id, IsPresent = false });

        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        Assert.Equal(VerificationResult.VerifiedRemoved, session.VerificationResult);
        Assert.Equal(123, session.ProcessId);
        Assert.Equal(0, session.ExitCode);
        
        _sessionRepoMock.Verify(r => r.SaveAsync(It.IsAny<UninstallSession>(), It.IsAny<CancellationToken>()), Times.Exactly(7));
    }

    [Fact]
    public async Task RunUninstallAsync_StillInstalled_TransitionsToFailed()
    {
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = "valid.exe" };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "valid.exe" };
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);
        _executorMock.Setup(e => e.ExecuteAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, ProcessId = 123 });
            
        _discoveryMock.Setup(d => d.DiscoverApplicationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult());

        _appRepoMock.Setup(r => r.GetByIdAsync(app.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Application { Id = app.Id, IsPresent = true });

        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Equal(VerificationResult.StillInstalled, session.VerificationResult);
        Assert.Contains("still installed", session.FailureReason);
    }
    
    [Fact]
    public async Task RunUninstallAsync_DiscoveryVerificationFails_TransitionsToFailed()
    {
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = "valid.exe" };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "valid.exe" };
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);
        _executorMock.Setup(e => e.ExecuteAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, ProcessId = 123 });
            
        _discoveryMock.Setup(d => d.DiscoverApplicationsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Registry access denied"));

        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Equal(VerificationResult.VerificationFailed, session.VerificationResult);
        Assert.Contains("Discovery verification failed", session.FailureReason);
    }

    [Fact]
    public async Task RunUninstallAsync_InvalidCommand_TransitionsToFailedDuringValidating()
    {
        var app = new Application { Id = Guid.NewGuid() };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Missing }; // Invalid command
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);

        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Contains("Command validation failed", session.FailureReason);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunUninstallAsync_ExecutionFailure_TransitionsToFailed()
    {
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = "error.exe" };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "error.exe" };
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);
        _executorMock.Setup(e => e.ExecuteAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ErrorMessage = "Process failed to start." });

        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Equal("Process failed to start.", session.FailureReason);
    }

    [Fact]
    public async Task RunUninstallAsync_NonZeroExitCode_TransitionsToFailedDuringVerification()
    {
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = "valid.exe" };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "valid.exe" };
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);
        _executorMock.Setup(e => e.ExecuteAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 1603 }); // Standard MSI error

        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Equal(VerificationResult.VerificationFailed, session.VerificationResult);
        Assert.Contains("non-zero code", session.FailureReason);
        Assert.Equal(1603, session.ExitCode);
    }

    [Fact]
    public async Task RunUninstallAsync_CancellationBeforeExecution_TransitionsToCancelled()
    {
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = "valid.exe" };
        var cmd = new StructuredCommand { ExecutionType = ExecutionType.Executable, ExecutablePath = "valid.exe" };
        
        _parserMock.Setup(p => p.Parse(app)).Returns(cmd);
        
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before calling

        var session = await _service.RunUninstallAsync(app, cts.Token);

        Assert.Equal(UninstallSessionStatus.Cancelled, session.Status);
        Assert.Contains("Cancelled before execution", session.FailureReason);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
