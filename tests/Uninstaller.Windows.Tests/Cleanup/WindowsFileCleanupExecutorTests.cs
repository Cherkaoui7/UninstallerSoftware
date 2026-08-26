using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Cleanup;
using Xunit;

namespace Uninstaller.Windows.Tests.Cleanup;

public class WindowsFileCleanupExecutorTests : IDisposable
{
    private readonly string _testTempRoot;
    private readonly Mock<ICanonicalPathResolver> _resolverMock;
    private readonly WindowsFileCleanupExecutor _executor;

    public WindowsFileCleanupExecutorTests()
    {
        _testTempRoot = Path.Combine(Path.GetTempPath(), "UninstallerExecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempRoot);

        _resolverMock = new Mock<ICanonicalPathResolver>();
        _executor = new WindowsFileCleanupExecutor(_resolverMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_AuthorizedFile_ShouldDeleteAndVerify()
    {
        var targetFile = Path.Combine(_testTempRoot, "file.txt");
        File.WriteAllText(targetFile, "data");

        var context = CreateContext(targetFile, ArtifactType.File);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetFile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetFile });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);
        File.Exists(targetFile).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingPreflightOrBackup_ShouldFailEarly()
    {
        var targetFile = Path.Combine(_testTempRoot, "file.txt");
        var context = CreateContext(targetFile, ArtifactType.File);
        context.PreflightOutcomeAuthorized = false;

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        _resolverMock.Verify(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedFile_ShouldFail()
    {
        var targetFile = Path.Combine(_testTempRoot, "protected.txt");
        File.WriteAllText(targetFile, "data");
        var context = CreateContext(targetFile, ArtifactType.File);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetFile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetFile, IsProtected = true });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Protected);
        File.Exists(targetFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ReparsePoint_ShouldFail()
    {
        var targetFile = Path.Combine(_testTempRoot, "reparse.txt");
        File.WriteAllText(targetFile, "data");
        var context = CreateContext(targetFile, ArtifactType.File);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetFile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetFile, IsReparsePoint = true });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ReparseBlocked);
        File.Exists(targetFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDirectory_ShouldDelete()
    {
        var targetDir = Path.Combine(_testTempRoot, "EmptyDir");
        Directory.CreateDirectory(targetDir);

        var context = CreateContext(targetDir, ArtifactType.Directory);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetDir, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetDir });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);
        Directory.Exists(targetDir).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_NonEmptyDirectory_ShouldFailCleanly()
    {
        var targetDir = Path.Combine(_testTempRoot, "NonEmptyDir");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "child.txt"), "data");

        var context = CreateContext(targetDir, ArtifactType.Directory);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetDir, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetDir });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.DirectoryNotEmpty);
        Directory.Exists(targetDir).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_LockedFile_ShouldFailCleanly()
    {
        var targetFile = Path.Combine(_testTempRoot, "locked.txt");
        File.WriteAllText(targetFile, "data");

        var context = CreateContext(targetFile, ArtifactType.File);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetFile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetFile });

        // Lock the file
        using var fs = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Locked);
        result.RequiresReboot.Should().BeFalse(); // Mandated for V1
    }

    private AuthorizedExecutionContext CreateContext(string path, ArtifactType type)
    {
        return new AuthorizedExecutionContext
        {
            CleanupPlanItemId = Guid.NewGuid(),
            CanonicalPath = path,
            ArtifactType = type,
            PreflightOutcomeAuthorized = true,
            BackupId = Guid.NewGuid(),
            BackupVerificationStatus = BackupVerificationStatus.Verified,
            ApplicationId = Guid.NewGuid(),
            ExecutionAuthorizationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempRoot))
        {
            Directory.Delete(_testTempRoot, true);
        }
    }
}
