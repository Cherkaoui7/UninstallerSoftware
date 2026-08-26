using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Moq;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Registry;
using Xunit;

namespace Uninstaller.Windows.Tests.Registry;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsRegistryScannerTests
{
    private readonly Mock<IRegistryProvider> _providerMock;
    private readonly WindowsRegistryScanner _scanner;

    public WindowsRegistryScannerTests()
    {
        _providerMock = new Mock<IRegistryProvider>();
        _scanner = new WindowsRegistryScanner(_providerMock.Object, NullLogger<WindowsRegistryService>.Instance);
    }

    [Fact]
    public async Task ScanAsync_WithExactAppMatch_ReturnsCandidate()
    {
        var app = new Application { Name = "TestApp" };
        var softwareMock = new Mock<IRegistryKeyWrapper>();
        softwareMock.Setup(k => k.GetSubKeyNames()).Returns(new[] { "TestApp", "UnrelatedApp" });
        
        var baseKeyMock = new Mock<IRegistryKeyWrapper>();
        baseKeyMock.Setup(k => k.OpenSubKey("Software", false)).Returns(softwareMock.Object);

        _providerMock.Setup(p => p.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)).Returns(baseKeyMock.Object);
        // Return null for others to simplify
        _providerMock.Setup(p => p.OpenBaseKey(RegistryHive.LocalMachine, It.IsAny<RegistryView>())).Returns((IRegistryKeyWrapper?)null);

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("TestApp", candidate.Artifact.Name);
        Assert.Contains(candidate.Evidence, e => e.Type == EvidenceType.ExactApplicationKeyMatch);
    }

    [Fact]
    public async Task ScanAsync_WithPublisherAndApp_ReturnsBoth()
    {
        var app = new Application { Name = "TestApp", Publisher = "TestPublisher" };
        
        var publisherMock = new Mock<IRegistryKeyWrapper>();
        publisherMock.Setup(k => k.GetSubKeyNames()).Returns(new[] { "TestApp", "OtherApp" });

        var softwareMock = new Mock<IRegistryKeyWrapper>();
        softwareMock.Setup(k => k.GetSubKeyNames()).Returns(new[] { "TestPublisher", "UnrelatedApp" });
        softwareMock.Setup(k => k.OpenSubKey("TestPublisher", false)).Returns(publisherMock.Object);
        
        var baseKeyMock = new Mock<IRegistryKeyWrapper>();
        baseKeyMock.Setup(k => k.OpenSubKey("Software", false)).Returns(softwareMock.Object);

        _providerMock.Setup(p => p.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)).Returns(baseKeyMock.Object);

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Artifact.Name == "TestPublisher" && c.Evidence.Any(e => e.Type == EvidenceType.ExactPublisherKeyMatch));
        Assert.Contains(candidates, c => c.Artifact.Name == "TestApp" && c.Evidence.Any(e => e.Type == EvidenceType.ExactApplicationKeyMatch));
    }

    [Fact]
    public async Task ScanAsync_FalsePositiveSubstring_IsIgnored()
    {
        var app = new Application { Name = "TestApp" };
        var softwareMock = new Mock<IRegistryKeyWrapper>();
        softwareMock.Setup(k => k.GetSubKeyNames()).Returns(new[] { "TestAppTools", "TestApplication" });
        
        var baseKeyMock = new Mock<IRegistryKeyWrapper>();
        baseKeyMock.Setup(k => k.OpenSubKey("Software", false)).Returns(softwareMock.Object);

        _providerMock.Setup(p => p.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)).Returns(baseKeyMock.Object);

        var candidates = await _scanner.ScanAsync(app, CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task ScanAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var app = new Application { Name = "TestApp" };
        
        var softwareMock = new Mock<IRegistryKeyWrapper>();
        softwareMock.Setup(k => k.GetSubKeyNames()).Returns(new[] { "TestApp" });
        
        var baseKeyMock = new Mock<IRegistryKeyWrapper>();
        baseKeyMock.Setup(k => k.OpenSubKey("Software", false)).Returns(softwareMock.Object);

        _providerMock.Setup(p => p.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)).Returns(baseKeyMock.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _scanner.ScanAsync(app, cts.Token));
    }
}
