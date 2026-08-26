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
using Uninstaller.Windows.Filesystem;
using Xunit;

namespace Uninstaller.Windows.Tests.Cleanup;

/// <summary>
/// Tests for WindowsShortcutCleanupExecutor.
///
/// Strategy:
///   - Real .lnk files are written into a temporary directory in %TEMP% that is
///     cleaned up by IDisposable.  No production shortcut locations are touched.
///   - IShortcutProvider and ICanonicalPathResolver are mocked with Moq.
/// </summary>
public sealed class WindowsShortcutCleanupExecutorTests : IDisposable
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly Mock<ICanonicalPathResolver> _pathResolverMock;
    private readonly Mock<IShortcutProvider> _shortcutProviderMock;
    private readonly WindowsShortcutCleanupExecutor _executor;

    public WindowsShortcutCleanupExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ShortcutExecutorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _pathResolverMock    = new Mock<ICanonicalPathResolver>(MockBehavior.Strict);
        _shortcutProviderMock = new Mock<IShortcutProvider>(MockBehavior.Loose);
        _executor = new WindowsShortcutCleanupExecutor(
            _pathResolverMock.Object,
            _shortcutProviderMock.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a real, empty .lnk file so File.Exists returns true.</summary>
    private string CreateFakeLnk(string name = "App.lnk")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private void SetupSafePathResolver(string canonicalPath, bool isProtected = false, bool isReparsePoint = false)
    {
        _pathResolverMock
            .Setup(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult
            {
                IsValid = true,
                CanonicalPath = canonicalPath,
                IsProtected = isProtected,
                IsReparsePoint = isReparsePoint,
                IsWithinExpectedRoot = true,
            });
    }

    private AuthorizedExecutionContext CreateContext(
        string path,
        string expectedTarget = "",
        bool preflightAuthorized = true,
        BackupVerificationStatus backupStatus = BackupVerificationStatus.Verified)
    {
        return new AuthorizedExecutionContext
        {
            CleanupPlanItemId = Guid.NewGuid(),
            CanonicalPath = path,
            ArtifactType = ArtifactType.Shortcut,
            PreflightOutcomeAuthorized = preflightAuthorized,
            BackupId = Guid.NewGuid(),
            BackupVerificationStatus = backupStatus,
            ApplicationId = Guid.NewGuid(),
            ExecutionAuthorizationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ExpectedShortcutTarget = expectedTarget,
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1. Authorized shortcut deletion
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_AuthorizedShortcut_DeletesAndVerifies()
    {
        var lnk = CreateFakeLnk();
        var target = @"C:\Program Files\App\app.exe";
        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(lnk))
            .Returns(new ShortcutInfo { TargetPath = target });

        var result = await _executor.ExecuteAsync(CreateContext(lnk, expectedTarget: target));

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);
        File.Exists(lnk).Should().BeFalse("the shortcut must have been deleted");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Missing preflight authorization
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_MissingPreflight_RejectsBeforeAnyIO()
    {
        var lnk = CreateFakeLnk("NoPreflight.lnk");
        var ctx = CreateContext(lnk, preflightAuthorized: false);

        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        File.Exists(lnk).Should().BeTrue("no mutation should have occurred");
        _pathResolverMock.Verify(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Missing / unverified backup
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_UnverifiedBackup_RejectsBeforeAnyIO()
    {
        var lnk = CreateFakeLnk("NoBackup.lnk");
        var ctx = CreateContext(lnk, backupStatus: BackupVerificationStatus.Unverified);

        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        File.Exists(lnk).Should().BeTrue();
        _pathResolverMock.Verify(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. Wrong artifact type
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_WrongArtifactType_RejectsWithValidationFailed()
    {
        var lnk = CreateFakeLnk("WrongType.lnk");
        var ctx = CreateContext(lnk);
        ctx.ArtifactType = ArtifactType.File;

        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        result.FailureReason.Should().Contain("ArtifactType.Shortcut");
        File.Exists(lnk).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. Protected shortcut path
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_ProtectedPath_ReturnsProtected()
    {
        var lnk = CreateFakeLnk("Protected.lnk");
        SetupSafePathResolver(lnk, isProtected: true);
        var ctx = CreateContext(lnk);

        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Protected);
        File.Exists(lnk).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 6. Reparse point
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_ReparsePoint_ReturnsReparseBlocked()
    {
        var lnk = CreateFakeLnk("Reparse.lnk");
        SetupSafePathResolver(lnk, isReparsePoint: true);
        var ctx = CreateContext(lnk);

        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ReparseBlocked);
        File.Exists(lnk).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 7. Changed shortcut path (canonical drift → IdentityMismatch)
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_CanonicalPathDrift_ReturnsIdentityMismatch()
    {
        var lnk = CreateFakeLnk("Drifted.lnk");
        var differentPath = Path.Combine(_tempDir, "Other.lnk");

        // Resolver returns a *different* canonical path — simulating a symlink redirect
        _pathResolverMock
            .Setup(r => r.ResolveAndVerify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new PathSafetyResult
            {
                IsValid = true,
                CanonicalPath = differentPath,
                IsProtected = false,
                IsReparsePoint = false,
            });

        var result = await _executor.ExecuteAsync(CreateContext(lnk));

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.IdentityMismatch);
        File.Exists(lnk).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 8. Changed target (IdentityMismatch)
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_ChangedTarget_ReturnsIdentityMismatch()
    {
        var lnk = CreateFakeLnk("ChangedTarget.lnk");
        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(lnk))
            .Returns(new ShortcutInfo { TargetPath = @"C:\SomeOtherApp\other.exe" });

        var ctx = CreateContext(lnk, expectedTarget: @"C:\Program Files\App\app.exe");
        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.IdentityMismatch);
        result.FailureReason.ToLowerInvariant().Should().Contain("target changed");
        File.Exists(lnk).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 9. Missing shortcut (NotFound)
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_ShortcutAlreadyGone_ReturnsNotFound()
    {
        var lnk = Path.Combine(_tempDir, "Missing.lnk"); // never created
        SetupSafePathResolver(lnk);

        var result = await _executor.ExecuteAsync(CreateContext(lnk));

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.NotFound);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 10. Non-.lnk extension rejected
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_NonLnkExtension_RejectsWithValidationFailed()
    {
        var txt = Path.Combine(_tempDir, "App.txt");
        File.WriteAllText(txt, "not a shortcut");
        SetupSafePathResolver(txt);
        var ctx = CreateContext(txt);
        ctx.CanonicalPath = txt;

        var result = await _executor.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        result.FailureReason.Should().Contain(".lnk");
        File.Exists(txt).Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 11. Target file remains untouched after successful shortcut deletion
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_SuccessfulDeletion_TargetFileRemainsUntouched()
    {
        var lnk = CreateFakeLnk("WithTarget.lnk");
        var targetFile = Path.Combine(_tempDir, "app.exe");
        File.WriteAllBytes(targetFile, new byte[] { 0x4D, 0x5A });

        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(lnk))
            .Returns(new ShortcutInfo { TargetPath = targetFile });

        var result = await _executor.ExecuteAsync(CreateContext(lnk, expectedTarget: targetFile));

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);
        File.Exists(lnk).Should().BeFalse("shortcut must be gone");
        File.Exists(targetFile).Should().BeTrue("target must remain untouched");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 12. Parent directory remains untouched
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_SuccessfulDeletion_ParentDirectoryRemains()
    {
        var subDir = Path.Combine(_tempDir, "SubFolder");
        Directory.CreateDirectory(subDir);
        var lnk = Path.Combine(subDir, "App.lnk");
        File.WriteAllBytes(lnk, Array.Empty<byte>());

        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(It.IsAny<string>())).Returns((ShortcutInfo?)null);

        var result = await _executor.ExecuteAsync(CreateContext(lnk, expectedTarget: ""));

        result.Success.Should().BeTrue();
        File.Exists(lnk).Should().BeFalse();
        Directory.Exists(subDir).Should().BeTrue("the parent directory must never be deleted");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 13. Neighboring shortcuts remain untouched
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_OnlyDeletesAuthorizedShortcut_NeighborsUntouched()
    {
        var authorized = CreateFakeLnk("Authorized.lnk");
        var neighbor1  = CreateFakeLnk("Neighbor1.lnk");
        var neighbor2  = CreateFakeLnk("Neighbor2.lnk");

        SetupSafePathResolver(authorized);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(It.IsAny<string>())).Returns((ShortcutInfo?)null);

        var result = await _executor.ExecuteAsync(CreateContext(authorized, expectedTarget: ""));

        result.Success.Should().BeTrue();
        File.Exists(authorized).Should().BeFalse();
        File.Exists(neighbor1).Should().BeTrue("neighbor shortcuts must be untouched");
        File.Exists(neighbor2).Should().BeTrue("neighbor shortcuts must be untouched");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 14. Cancellation before mutation
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_CancelledBeforeStart_ReturnsCancelled()
    {
        var lnk = CreateFakeLnk("Cancelled.lnk");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _executor.ExecuteAsync(CreateContext(lnk), cts.Token);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Cancelled);
        File.Exists(lnk).Should().BeTrue("no mutation should occur after cancellation");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 15. Final validation is always performed
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_ValidPath_FinalValidationPerformed()
    {
        var lnk = CreateFakeLnk("FinalVal.lnk");
        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(It.IsAny<string>())).Returns((ShortcutInfo?)null);

        var result = await _executor.ExecuteAsync(CreateContext(lnk, expectedTarget: ""));

        result.WasFinalValidationPerformed.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 16. Security search — forbidden symbols must not appear in executor source
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void SecuritySearch_ExecutorClassDoesNotContainForbiddenSymbols()
    {
        var executorSource = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Uninstaller.Windows", "Cleanup", "WindowsShortcutCleanupExecutor.cs"));

        executorSource.Should().NotContain("Process.Start",    because: "no process execution allowed");
        executorSource.Should().NotContain("ShellExecute",     because: "no ShellExecute allowed");
        executorSource.Should().NotContain("cmd.exe",          because: "no cmd shell allowed");
        executorSource.Should().NotContain("powershell",       because: "no PowerShell allowed");
        executorSource.Should().NotContain("Directory.Delete", because: "no directory deletion allowed");
        executorSource.Should().NotContain("RegistryKey",      because: "no registry mutation allowed");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 17. No registry interaction (structural: IRegistryService not injected)
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_NoRegistryInteractionRequired()
    {
        var lnk = CreateFakeLnk("NoRegistry.lnk");
        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(It.IsAny<string>())).Returns((ShortcutInfo?)null);

        var result = await _executor.ExecuteAsync(CreateContext(lnk, expectedTarget: ""));

        result.Success.Should().BeTrue(
            "a successful shortcut deletion requires no registry interaction whatsoever");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 18. Result flags set correctly on success
    // ══════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_StatusFlagsSetCorrectly()
    {
        var lnk = CreateFakeLnk("Flags.lnk");
        SetupSafePathResolver(lnk);
        _shortcutProviderMock.Setup(s => s.GetShortcutInfo(It.IsAny<string>())).Returns((ShortcutInfo?)null);

        var result = await _executor.ExecuteAsync(CreateContext(lnk, expectedTarget: ""));

        result.WasPreflightValidated.Should().BeTrue();
        result.WasBackupVerified.Should().BeTrue();
        result.WasFinalValidationPerformed.Should().BeTrue();
        result.RequiresReboot.Should().BeFalse();
    }
}
