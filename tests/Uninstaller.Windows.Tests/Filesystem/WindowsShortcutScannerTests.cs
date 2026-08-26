using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Filesystem;
using Xunit;

namespace Uninstaller.Windows.Tests.Filesystem;

public class WindowsShortcutScannerTests
{
    private readonly MockFileSystem _fileSystem;
    private readonly Mock<IShortcutProvider> _shortcutProviderMock;
    private readonly WindowsShortcutScanner _scanner;

    public WindowsShortcutScannerTests()
    {
        _fileSystem = new MockFileSystem();
        _shortcutProviderMock = new Mock<IShortcutProvider>();
        _scanner = new WindowsShortcutScanner(_fileSystem, _shortcutProviderMock.Object, NullLogger<WindowsShortcutScanner>.Instance);
    }

    [Fact]
    public async Task ScanAsync_WithExactInstallLocationTarget_ReturnsCandidate()
    {
        var app = new Application { Name = "TestApp", InstallLocation = @"C:\Program Files\TestApp" };
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = _fileSystem.Path.Combine(desktopPath, "Launch App.lnk");

        _fileSystem.AddDirectory(desktopPath);
        _fileSystem.AddFile(shortcutPath, new MockFileData("dummy"));
        _fileSystem.AddDirectory(app.InstallLocation);

        _shortcutProviderMock.Setup(p => p.GetShortcutInfo(shortcutPath)).Returns(new ShortcutInfo
        {
            TargetPath = app.InstallLocation
        });

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Launch App", candidate.Artifact.Name);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.ExactShortcutTarget);
    }

    [Fact]
    public async Task ScanAsync_WithMatchingShortcutName_ReturnsCandidate()
    {
        var app = new Application { Name = "Test App" };
        var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var shortcutPath = _fileSystem.Path.Combine(startMenuPath, "TestApp.lnk");

        _fileSystem.AddDirectory(startMenuPath);
        _fileSystem.AddFile(shortcutPath, new MockFileData("dummy"));
        _fileSystem.AddFile(@"C:\Some\Other\Path.exe", new MockFileData("dummy"));

        _shortcutProviderMock.Setup(p => p.GetShortcutInfo(shortcutPath)).Returns(new ShortcutInfo
        {
            TargetPath = @"C:\Some\Other\Path.exe"
        });

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.ShortcutNameMatch);
    }

    [Fact]
    public async Task ScanAsync_WithTargetUnderInstallLocation_ReturnsCandidate()
    {
        var app = new Application { Name = "TestApp", InstallLocation = @"C:\Program Files\TestApp" };
        var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var shortcutPath = _fileSystem.Path.Combine(startMenuPath, "Start.lnk");
        var targetExe = @"C:\Program Files\TestApp\Bin\app.exe";

        _fileSystem.AddDirectory(startMenuPath);
        _fileSystem.AddFile(shortcutPath, new MockFileData("dummy"));
        _fileSystem.AddFile(targetExe, new MockFileData("dummy"));

        _shortcutProviderMock.Setup(p => p.GetShortcutInfo(shortcutPath)).Returns(new ShortcutInfo
        {
            TargetPath = targetExe
        });

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.InstallLocationTargetMatch);
    }

    [Fact]
    public async Task ScanAsync_FalsePositiveSubstring_IsIgnored()
    {
        var app = new Application { Name = "MyApp" };
        var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var shortcutPath = _fileSystem.Path.Combine(startMenuPath, "MyApplicationHelp.lnk");

        _fileSystem.AddDirectory(startMenuPath);
        _fileSystem.AddFile(shortcutPath, new MockFileData("dummy"));

        _shortcutProviderMock.Setup(p => p.GetShortcutInfo(shortcutPath)).Returns(new ShortcutInfo
        {
            TargetPath = @"C:\Other\app.exe"
        });

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task ScanAsync_BrokenShortcut_AddsBrokenEvidence()
    {
        var app = new Application { Name = "TestApp" };
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = _fileSystem.Path.Combine(desktopPath, "TestApp.lnk");

        _fileSystem.AddDirectory(desktopPath);
        _fileSystem.AddFile(shortcutPath, new MockFileData("dummy"));

        _shortcutProviderMock.Setup(p => p.GetShortcutInfo(shortcutPath)).Returns(new ShortcutInfo
        {
            TargetPath = @"C:\NonExistent\app.exe"
        });

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.BrokenShortcutTarget);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.ShortcutNameMatch);
    }

    [Fact]
    public async Task ScanAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var app = new Application { Name = "TestApp" };
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = _fileSystem.Path.Combine(desktopPath, "TestApp.lnk");

        _fileSystem.AddDirectory(desktopPath);
        _fileSystem.AddFile(shortcutPath, new MockFileData("dummy"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _scanner.ScanAsync(app, cts.Token));
    }
}
