using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Win32;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Cleanup;
using Xunit;

namespace Uninstaller.Windows.Tests.Cleanup;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsRegistryCleanupExecutorTests : IDisposable
{
    private readonly WindowsRegistryCleanupExecutor _executor;
    private readonly string _testBaseKeyName;

    public WindowsRegistryCleanupExecutorTests()
    {
        _executor = new WindowsRegistryCleanupExecutor();
        _testBaseKeyName = "TestCleanupApp_" + Guid.NewGuid().ToString("N");
    }

    [Fact]
    public async Task ExecuteAsync_AuthorizedRegistryKey_ShouldDeleteAndVerify()
    {
        var targetPath = $@"HKCU\Software\{_testBaseKeyName}_1";
        using var baseKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\{_testBaseKeyName}_1");
        
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);
        
        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);

        using var checkKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\{_testBaseKeyName}_1");
        checkKey.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AuthorizedRegistryValue_ShouldDeleteAndVerify()
    {
        var keyPath = $@"Software\{_testBaseKeyName}_2";
        using var baseKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
        baseKey.SetValue("MyValue", "Hello");

        var targetPath = $@"HKCU\{keyPath}::MyValue";
        var context = CreateContext(targetPath, ArtifactType.RegistryValue);

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(CleanupOutcome.DeletedAndVerified);

        using var checkKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
        checkKey.Should().NotBeNull();
        checkKey.GetValue("MyValue").Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MissingPreflightOrBackup_ShouldFailEarly()
    {
        var targetPath = $@"HKCU\Software\{_testBaseKeyName}_3";
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);
        context.PreflightOutcomeAuthorized = false;

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedRoot_ShouldFail()
    {
        var targetPath = $@"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall";
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Protected);
    }

    [Fact]
    public async Task ExecuteAsync_ProtectedRootVariation_ShouldFail()
    {
        var targetPath = $@"HKCU\SOFTWARE"; // case insensitivity check
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.Protected);
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentKey_ShouldReturnNotFound()
    {
        var targetPath = $@"HKCU\Software\{_testBaseKeyName}_DoesNotExist";
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.NotFound);
    }

    [Fact]
    public async Task ExecuteAsync_HiveMismatch_ShouldFail()
    {
        var targetPath = $@"HKCU\Software\{_testBaseKeyName}_4";
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);
        // Claim it was authorized for HKLM — executor must reject
        context.ExpectedRegistryHive = "HKLM";
        context.ExpectedRegistryKeyPath = $@"Software\{_testBaseKeyName}_4";

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        result.FailureReason.Should().Contain("hive mismatch");
    }

    [Fact]
    public async Task ExecuteAsync_KeyPathMismatch_ShouldFail()
    {
        var targetPath = $@"HKCU\Software\{_testBaseKeyName}_5";
        var context = CreateContext(targetPath, ArtifactType.RegistryKey);
        // Claim it was authorized for a different subkey
        context.ExpectedRegistryHive = "HKCU";
        context.ExpectedRegistryKeyPath = $@"Software\{_testBaseKeyName}_Different";

        var result = await _executor.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Outcome.Should().Be(CleanupOutcome.ValidationFailed);
        result.FailureReason.Should().Contain("changed between authorization and execution");
    }

    private AuthorizedExecutionContext CreateContext(string path, ArtifactType type)
    {
        // Parse hive and key path from the canonical path so tests populate identity correctly
        var parts = path.Split('\\', 2);
        var hive = parts.Length > 0 ? parts[0] : string.Empty;
        // For value paths (KeyPath::ValueName), strip the value name for ExpectedRegistryKeyPath
        var keyPath = parts.Length > 1 ? parts[1].Split("::")[0] : string.Empty;

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
            CreatedAt = DateTime.UtcNow,
            ExpectedRegistryHive = hive,
            ExpectedRegistryKeyPath = keyPath
        };
    }

    public void Dispose()
    {
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\{_testBaseKeyName}_1", false); } catch { }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\{_testBaseKeyName}_2", false); } catch { }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\{_testBaseKeyName}_3", false); } catch { }
    }
}
