using System;
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

public class UninstallServiceProductionPathTests
{
    private readonly Mock<IUninstallSessionRepository> _sessionRepoMock;
    private readonly Mock<IApplicationRepository> _appRepoMock;
    private readonly Mock<IFileSystemService> _fileSystemMock;
    private readonly Mock<IProcessExecutor> _executorMock;
    private readonly Mock<IDiscoveryService> _discoveryMock;
    private readonly UninstallService _service;
    private readonly CommandParser _realParser;

    public UninstallServiceProductionPathTests()
    {
        _sessionRepoMock = new Mock<IUninstallSessionRepository>();
        _appRepoMock = new Mock<IApplicationRepository>();
        _fileSystemMock = new Mock<IFileSystemService>();
        _executorMock = new Mock<IProcessExecutor>();
        _discoveryMock = new Mock<IDiscoveryService>();
        
        _realParser = new CommandParser(_fileSystemMock.Object, NullLogger<CommandParser>.Instance);
        
        _service = new UninstallService(
            _sessionRepoMock.Object,
            _appRepoMock.Object,
            _realParser,
            _executorMock.Object,
            _discoveryMock.Object,
            NullLogger<UninstallService>.Instance
        );

        // Setup process executor to succeed by default so it passes Validation and reaches Execution
        _executorMock.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, ProcessId = 123 });
            
        _discoveryMock.Setup(d => d.DiscoverApplicationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult());
            
        // Mock repo returning null or IsPresent = false so Verification succeeds
        _appRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => new Application { Id = id, IsPresent = false });
    }

    [Fact]
    public async Task RunUninstallAsync_ExactQuotedExecutable_PassesValidation()
    {
        // 1. Exact quoted executable with literal backslashes, matching E2E-App-001
        var commandStr = "\"C:\\Uninstaller-E2E-TestRoot\\E2E-App-001\\Uninstaller\\E2E-App-001-Uninstaller.exe\"";
        _fileSystemMock.Setup(fs => fs.FileExists("C:\\Uninstaller-E2E-TestRoot\\E2E-App-001\\Uninstaller\\E2E-App-001-Uninstaller.exe")).Returns(true);

        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        
        var session = await _service.RunUninstallAsync(app);

        // A. Exact failing validation condition
        // In production, this fails validation. We assert it MUST pass validation and reach Completed.
        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        
        // C. Exact production call chain verifies the executor receives the PARSED command without quotes
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<StructuredCommand>(c => 
            c.ExecutablePath == "C:\\Uninstaller-E2E-TestRoot\\E2E-App-001\\Uninstaller\\E2E-App-001-Uninstaller.exe" &&
            c.ExecutionType == ExecutionType.Executable &&
            c.IsValid == true
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunUninstallAsync_QuotedExecutable_NoArguments_PassesValidation()
    {
        var commandStr = "\"C:\\Program Files\\App\\AppUninstaller.exe\"";
        _fileSystemMock.Setup(fs => fs.FileExists("C:\\Program Files\\App\\AppUninstaller.exe")).Returns(true);

        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<StructuredCommand>(c => 
            c.ExecutablePath == "C:\\Program Files\\App\\AppUninstaller.exe" && c.Arguments == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunUninstallAsync_QuotedExecutable_WithArguments_PassesValidation()
    {
        var commandStr = "\"C:\\Program Files\\App\\AppUninstaller.exe\" /silent /cleanup";
        _fileSystemMock.Setup(fs => fs.FileExists("C:\\Program Files\\App\\AppUninstaller.exe")).Returns(true);

        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<StructuredCommand>(c => 
            c.ExecutablePath == "C:\\Program Files\\App\\AppUninstaller.exe" && c.Arguments == "/silent /cleanup"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunUninstallAsync_UnquotedExecutable_PassesValidation()
    {
        var commandStr = "C:\\Program Files\\App Folder\\uninst.exe /S";
        _fileSystemMock.Setup(fs => fs.FileExists("C:\\Program Files\\App Folder\\uninst.exe")).Returns(true);

        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Completed, session.Status);
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<StructuredCommand>(c => 
            c.ExecutablePath == "C:\\Program Files\\App Folder\\uninst.exe" && c.Arguments == "/S"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunUninstallAsync_MissingExecutable_FailsValidation()
    {
        var commandStr = "\"C:\\App\\Missing.exe\" /S";
        // Mock file system returning FALSE for this path
        _fileSystemMock.Setup(fs => fs.FileExists("C:\\App\\Missing.exe")).Returns(false);

        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Contains("Command validation failed", session.FailureReason);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunUninstallAsync_MalformedExecutable_FailsValidation()
    {
        var commandStr = "\"C:\\App\\Missing.exe /S"; // Missing closing quote
        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        
        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Contains("Command validation failed", session.FailureReason);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunUninstallAsync_ForbiddenShellCommand_FailsValidation()
    {
        var commandStr = "cmd.exe /c del C:\\test.txt";
        _fileSystemMock.Setup(fs => fs.FileExists("cmd.exe")).Returns(true);

        var app = new Application { Id = Guid.NewGuid(), UninstallCommand = commandStr };
        var session = await _service.RunUninstallAsync(app);

        Assert.Equal(UninstallSessionStatus.Failed, session.Status);
        Assert.Contains("Command validation failed", session.FailureReason);
        _executorMock.Verify(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
