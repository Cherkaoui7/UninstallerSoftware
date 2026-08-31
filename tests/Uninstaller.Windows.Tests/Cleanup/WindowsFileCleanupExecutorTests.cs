using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        _resolverMock.Setup(r => r.IsPathContainedWithin(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string p, string root) => 
            {
                var normP = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
                var normR = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                return normP.StartsWith(normR + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || normP.Equals(normR, StringComparison.OrdinalIgnoreCase);
            });

        _executor = new WindowsFileCleanupExecutor(_resolverMock.Object, NullLogger<WindowsFileCleanupExecutor>.Instance);
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
    public async Task ExecuteAsync_DirectoryWithFiles_ShouldDeleteSafelyAndVerify()
    {
        var targetDir = Path.Combine(_testTempRoot, "Telegram Desktop");
        Directory.CreateDirectory(targetDir);
        var logFile = Path.Combine(targetDir, "log_start0.txt");
        File.WriteAllText(logFile, "Telegram log content");

        var context = CreateContext(targetDir, ArtifactType.Directory);

        _resolverMock.Setup(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string root, CancellationToken ct) => new PathSafetyResult { IsValid = true, CanonicalPath = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);
        Directory.Exists(targetDir).Should().BeFalse();
        File.Exists(logFile).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DirectoryWithNestedHierarchy_ShouldDeleteAllLevelsSafely()
    {
        var targetDir = Path.Combine(_testTempRoot, "AppDirectory");
        var subDir1 = Path.Combine(targetDir, "SubDir1");
        var subDir2 = Path.Combine(subDir1, "SubDir2");
        Directory.CreateDirectory(subDir2);
        
        var file1 = Path.Combine(targetDir, "root_file.bin");
        var file2 = Path.Combine(subDir1, "sub1_file.bin");
        var file3 = Path.Combine(subDir2, "sub2_file.bin");
        File.WriteAllText(file1, "1");
        File.WriteAllText(file2, "2");
        File.WriteAllText(file3, "3");

        var context = CreateContext(targetDir, ArtifactType.Directory);

        _resolverMock.Setup(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string root, CancellationToken ct) => new PathSafetyResult { IsValid = true, CanonicalPath = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);
        Directory.Exists(targetDir).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DirectoryWithProtectedChild_ShouldAbortFailClosed()
    {
        var targetDir = Path.Combine(_testTempRoot, "AppDirectoryWithProtectedChild");
        Directory.CreateDirectory(targetDir);
        var sensitiveFile = Path.Combine(targetDir, "sensitive.txt");
        File.WriteAllText(sensitiveFile, "protected data");

        var context = CreateContext(targetDir, ArtifactType.Directory);

        _resolverMock.Setup(r => r.ResolveAndVerify(targetDir, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = targetDir });

        _resolverMock.Setup(r => r.ResolveAndVerify(sensitiveFile, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult { IsValid = true, CanonicalPath = sensitiveFile, IsProtected = true });

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Protected);
        result.FailureReason.Should().Contain("protected child");
        Directory.Exists(targetDir).Should().BeTrue();
        File.Exists(sensitiveFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DirectoryWithLockedFile_ShouldFailCleanly()
    {
        var targetDir = Path.Combine(_testTempRoot, "DirWithLockedFile");
        Directory.CreateDirectory(targetDir);
        var lockedFile = Path.Combine(targetDir, "locked.log");
        File.WriteAllText(lockedFile, "log data");

        var context = CreateContext(targetDir, ArtifactType.Directory);

        _resolverMock.Setup(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string root, CancellationToken ct) => new PathSafetyResult { IsValid = true, CanonicalPath = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) });

        using (var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await _executor.ExecuteAsync(context);

            result.Success.Should().BeFalse();
            result.Outcome.Should().Be(CleanupOutcome.Locked);
            Directory.Exists(targetDir).Should().BeTrue();
        }
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
        result.RequiresReboot.Should().BeFalse();
    }

    private AuthorizedExecutionContext CreateContext(string path, ArtifactType type)
    {
        return new AuthorizedExecutionContext
        {
            CleanupPlanItemId = Guid.NewGuid(),
            CanonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
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
            try { Directory.Delete(_testTempRoot, true); } catch { }
        }
    }
}
