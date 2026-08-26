using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Win32;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Backup;
using Xunit;

namespace Uninstaller.Windows.Tests.Backups;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsRegistryBackupProviderTests : IDisposable
{
    private readonly Mock<IBackupStorage> _storageMock;
    private readonly WindowsRegistryBackupProvider _provider;
    private readonly string _testTempRoot;
    private readonly string _backupRoot;
    private readonly string _testKeyPath;

    public WindowsRegistryBackupProviderTests()
    {
        _testTempRoot = Path.Combine(Path.GetTempPath(), "UninstallerRegTests", Guid.NewGuid().ToString("N"));
        _backupRoot = Path.Combine(_testTempRoot, "Backups");
        Directory.CreateDirectory(_testTempRoot);
        Directory.CreateDirectory(_backupRoot);

        _storageMock = new Mock<IBackupStorage>();
        _storageMock.Setup(s => s.IsPathWithinControlledRoot(It.IsAny<string>())).Returns(true);
        _storageMock.Setup(s => s.GetBackupRoot()).Returns(_backupRoot);

        _provider = new WindowsRegistryBackupProvider(_storageMock.Object);
        
        var uniqueName = "TestBackupApp_" + Guid.NewGuid().ToString("N");
        _testKeyPath = $@"Software\{uniqueName}";

        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_testKeyPath);
        key.SetValue("StringValue", "Hello");
        key.SetValue("DWordValue", 42, RegistryValueKind.DWord);
        key.SetValue("BinaryValue", new byte[] { 0x01, 0x02 }, RegistryValueKind.Binary);
    }

    [Fact]
    public async Task BackupRegistryArtifactAsync_ShouldExportKey_AndPreserveTypes()
    {
        var item = new CleanupPlanItem { ArtifactType = ArtifactType.RegistryKey, Path = $@"HKCU\{_testKeyPath}" };

        var backup = await _provider.BackupRegistryArtifactAsync(item, _backupRoot, default);

        backup.Status.Should().Be(BackupStatus.Verifying);
        File.Exists(backup.BackupPath).Should().BeTrue();

        var content = File.ReadAllText(backup.BackupPath);
        content.Should().Contain("StringValue");
        content.Should().Contain("DWordValue");
        content.Should().Contain("BinaryValue");
        
        // Native source untouched
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_testKeyPath);
        key.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyRegistryBackupAsync_ShouldValidateExportedHash()
    {
        var item = new CleanupPlanItem { ArtifactType = ArtifactType.RegistryKey, Path = $@"HKCU\{_testKeyPath}" };
        
        var backup = await _provider.BackupRegistryArtifactAsync(item, _backupRoot, default);
        var verify = await _provider.VerifyRegistryBackupAsync(backup, default);

        verify.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreRegistryBackupAsync_ShouldPerformPerfectRoundTrip()
    {
        var item = new CleanupPlanItem { ArtifactType = ArtifactType.RegistryKey, Path = $@"HKCU\{_testKeyPath}" };
        
        var backup = await _provider.BackupRegistryArtifactAsync(item, _backupRoot, default);
        var verify = await _provider.VerifyRegistryBackupAsync(backup, default);
        verify.IsValid.Should().BeTrue();

        var restoreKeyPath = _testKeyPath + "_Restored";
        var restoreRoot = $@"HKCU\{restoreKeyPath}";

        await _provider.RestoreRegistryBackupAsync(backup, restoreRoot, default);

        using var restoredKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(restoreKeyPath);
        restoredKey.Should().NotBeNull();

        restoredKey.GetValue("StringValue").Should().Be("Hello");
        restoredKey.GetValueKind("StringValue").Should().Be(RegistryValueKind.String);

        restoredKey.GetValue("DWordValue").Should().Be(42);
        restoredKey.GetValueKind("DWordValue").Should().Be(RegistryValueKind.DWord);

        (restoredKey.GetValue("BinaryValue") as byte[])!.Should().BeEquivalentTo(new byte[] { 0x01, 0x02 });
        restoredKey.GetValueKind("BinaryValue").Should().Be(RegistryValueKind.Binary);

        // Cleanup the restored key
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(restoreKeyPath, throwOnMissingSubKey: false);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        if (Directory.Exists(_testTempRoot))
        {
            Directory.Delete(_testTempRoot, true);
        }
    }
}
