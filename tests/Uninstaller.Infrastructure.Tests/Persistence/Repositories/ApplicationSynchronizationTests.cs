using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Infrastructure.Persistence;
using Uninstaller.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Uninstaller.Infrastructure.Tests.Persistence.Repositories;

public class ApplicationSynchronizationTests
{
    private readonly AppDbContext _dbContext;
    private readonly ApplicationRepository _repository;
    private readonly ApplicationDeduplicator _deduplicator;
    private readonly ApplicationNormalizer _normalizer;
    private readonly DiscoveryService _discoveryService;
    private readonly Mock<IRegistryService> _registryMock;
    private readonly Mock<IFileSystemService> _fileSystemMock;
    private readonly UninstallService _uninstallService;
    private readonly CommandParser _commandParser;
    private readonly Mock<IProcessExecutor> _executorMock;

    public ApplicationSynchronizationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        _dbContext = new AppDbContext(options);
        _deduplicator = new ApplicationDeduplicator();
        _repository = new ApplicationRepository(_dbContext, _deduplicator, NullLogger<ApplicationRepository>.Instance);
        
        _registryMock = new Mock<IRegistryService>();
        _normalizer = new ApplicationNormalizer(NullLogger<ApplicationNormalizer>.Instance);
        
        _discoveryService = new DiscoveryService(
            _registryMock.Object,
            _normalizer,
            _repository,
            NullLogger<DiscoveryService>.Instance
        );

        _fileSystemMock = new Mock<IFileSystemService>();
        _fileSystemMock.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _commandParser = new CommandParser(_fileSystemMock.Object, NullLogger<CommandParser>.Instance);
        
        _executorMock = new Mock<IProcessExecutor>();
        _executorMock.Setup(e => e.ExecuteAsync(It.IsAny<StructuredCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, ProcessId = 123 });

        _uninstallService = new UninstallService(
            new Mock<IUninstallSessionRepository>().Object,
            _repository,
            _commandParser,
            _executorMock.Object,
            _discoveryService,
            NullLogger<UninstallService>.Instance
        );
    }

    [Fact]
    public async Task FullSynchronization_UpdatesUninstallCommand_AndBlocksPowershell()
    {
        // 1. Initial discovery stores UninstallCommand
        var initialRegistry = new List<RawRegistryApplication>
        {
            new RawRegistryApplication
            {
                DisplayName = "Test App",
                UninstallString = "\"C:\\old\\uninstall.exe\"",
                RegistryKeyName = "TestAppKey",
                Publisher = "Test Publisher",
                DisplayVersion = "1.0"
            }
        };
        _registryMock.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(initialRegistry);

        await _discoveryService.DiscoverApplicationsAsync();
        
        var apps = await _repository.GetAllAsync(CancellationToken.None);
        var app = Assert.Single(apps);
        Assert.Equal("\"C:\\old\\uninstall.exe\"", app.UninstallCommand);
        var initialAppId = app.Id;

        // 2. Registry command changes to a new valid command
        var updatedRegistry = new List<RawRegistryApplication>
        {
            new RawRegistryApplication
            {
                DisplayName = "Test App",
                UninstallString = "\"C:\\new\\uninstall.exe\"", // Changed
                InstallLocation = "C:\\new\\path", // Changed
                RegistryKeyName = "TestAppKey",
                Publisher = "Test Publisher", // Keeps heuristic match
                DisplayVersion = "1.0"
            }
        };
        _registryMock.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(updatedRegistry);

        // 3. Second discovery refresh updates Application.UninstallCommand
        await _discoveryService.DiscoverApplicationsAsync();

        // 4. Repository reload returns the new command
        // We use a fresh context query to ensure we're reading from DB, though in-memory EF shares state.
        var refreshedApps = await _repository.GetAllAsync(CancellationToken.None);
        var refreshedApp = Assert.Single(refreshedApps);
        
        Assert.Equal(initialAppId, refreshedApp.Id); // ID should remain the same (deduplication merges)
        Assert.Equal("\"C:\\new\\uninstall.exe\"", refreshedApp.UninstallCommand);
        Assert.Equal("C:\\new\\path", refreshedApp.InstallLocation);
        Assert.True(refreshedApp.IsPresent);

        // 5. UninstallService receives the updated command
        // Simulate app removal in registry so verification passes
        _registryMock.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<RawRegistryApplication>());

        // Execute an uninstall session and verify the CommandParser and Executor receive the NEW path
        var session1 = await _uninstallService.RunUninstallAsync(refreshedApp);
        Assert.Equal(Uninstaller.Domain.Enums.UninstallSessionStatus.Completed, session1.Status);
        
        _executorMock.Verify(e => e.ExecuteAsync(It.Is<StructuredCommand>(c => 
            c.ExecutablePath == "C:\\new\\uninstall.exe"), It.IsAny<CancellationToken>()), Times.Once);

        // 6. Change registry to blocked powershell.exe and refresh
        var blockedRegistry = new List<RawRegistryApplication>
        {
            new RawRegistryApplication
            {
                DisplayName = "Test App",
                UninstallString = "powershell.exe -NoProfile -File C:\\uninstall.ps1",
                RegistryKeyName = "TestAppKey",
                Publisher = "Test Publisher",
                DisplayVersion = "1.0"
            }
        };
        _registryMock.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(blockedRegistry);
        await _discoveryService.DiscoverApplicationsAsync();
        
        var blockedApp = (await _repository.GetAllAsync(CancellationToken.None)).Single();
        Assert.Equal("powershell.exe -NoProfile -File C:\\uninstall.ps1", blockedApp.UninstallCommand);
        
        // Uninstall should FAIL security validation because powershell is blocked
        var session2 = await _uninstallService.RunUninstallAsync(blockedApp);
        Assert.Equal(Uninstaller.Domain.Enums.UninstallSessionStatus.Failed, session2.Status);
        Assert.Contains("Command validation failed", session2.FailureReason);

        // 7. Remove uninstall command in registry and check fields are not accidentally overwritten incorrectly
        var removedCommandRegistry = new List<RawRegistryApplication>
        {
            new RawRegistryApplication
            {
                DisplayName = "Test App",
                UninstallString = null, // Removed
                RegistryKeyName = "TestAppKey",
                Publisher = "Test Publisher",
                DisplayVersion = "1.0" 
            }
        };
        _registryMock.Setup(r => r.GetUninstallEntriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(removedCommandRegistry);
        await _discoveryService.DiscoverApplicationsAsync();

        var noCommandApp = (await _repository.GetAllAsync(CancellationToken.None)).Single();
        // The old command was powershell.exe. Since it is removed in registry (null), does the DB keep the old one?
        // Our deduplicator currently does: target.UninstallCommand = source.UninstallCommand ?? target.UninstallCommand;
        // This means it will keep the old one. We will assert this behavior.
        Assert.Equal("powershell.exe -NoProfile -File C:\\uninstall.ps1", noCommandApp.UninstallCommand);
        Assert.Equal("1.0", noCommandApp.Version);
    }
}
