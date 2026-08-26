using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Filesystem;
using Xunit;

namespace Uninstaller.Windows.Tests.Filesystem;

public class WindowsFilesystemScannerTests
{
    private readonly MockFileSystem _fileSystem;
    private readonly WindowsFilesystemScanner _scanner;

    public WindowsFilesystemScannerTests()
    {
        _fileSystem = new MockFileSystem();
        _scanner = new WindowsFilesystemScanner(_fileSystem, NullLogger<WindowsFilesystemScanner>.Instance);
    }

    [Fact]
    public async Task ScanAsync_WithInstallLocation_ReturnsExactMatch()
    {
        _fileSystem.AddDirectory("C:\\Program Files\\TestApp");
        var app = new Application { Name = "TestApp", InstallLocation = "C:\\Program Files\\TestApp" };

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("C:\\Program Files\\TestApp", candidate.Artifact.Path);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.ExactInstallLocation);
    }

    [Fact]
    public async Task ScanAsync_AppDataTargeted_FindsAppAndPublisherFolders()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pubPath = _fileSystem.Path.Combine(localAppData, "TestPublisher");
        var appPath = _fileSystem.Path.Combine(pubPath, "TestApp");
        
        _fileSystem.AddDirectory(pubPath);
        _fileSystem.AddDirectory(appPath);

        // A false positive substring
        _fileSystem.AddDirectory(_fileSystem.Path.Combine(localAppData, "TestAppTools"));

        var app = new Application { Name = "TestApp", Publisher = "TestPublisher" };

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        Assert.Equal(2, candidates.Count); // Should find publisher dir and app dir
        Assert.Contains(candidates, c => c.Artifact.Path == pubPath && c.Evidence.Any(e => e.Type == EvidenceType.PublisherDirectoryMatch));
        Assert.Contains(candidates, c => c.Artifact.Path == appPath && c.Evidence.Any(e => e.Type == EvidenceType.ApplicationNameDirectoryMatch));
        
        // Ensure "TestAppTools" was ignored
        Assert.DoesNotContain(candidates, c => c.Artifact.Path.Contains("TestAppTools"));
    }

    [Fact]
    public async Task ScanAsync_NormalizedMatching_FindsVariants()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var appPath = _fileSystem.Path.Combine(programData, "Test App v1.0");
        
        _fileSystem.AddDirectory(appPath);

        // Normalize string will turn "Test App v1.0" into "testappv10"
        var app = new Application { Name = "TestAppv10" };

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(appPath, candidate.Artifact.Path);
    }

    [Fact]
    public async Task ScanAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var app = new Application { Name = "TestApp" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _scanner.ScanAsync(app, cts.Token));
    }
}
