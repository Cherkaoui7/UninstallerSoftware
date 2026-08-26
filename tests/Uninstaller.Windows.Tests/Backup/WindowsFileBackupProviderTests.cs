using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Backup;
using Xunit;

namespace Uninstaller.Windows.Tests.Backups;

public class WindowsFileBackupProviderTests : IDisposable
{
    private readonly Mock<ICanonicalPathResolver> _pathResolverMock;
    private readonly Mock<IBackupStorage> _storageMock;
    private readonly WindowsFileBackupProvider _provider;
    private readonly string _testTempRoot;
    private readonly string _backupRoot;

    public WindowsFileBackupProviderTests()
    {
        _testTempRoot = Path.Combine(Path.GetTempPath(), "UninstallerTests", Guid.NewGuid().ToString("N"));
        _backupRoot = Path.Combine(_testTempRoot, "Backups");
        Directory.CreateDirectory(_testTempRoot);
        Directory.CreateDirectory(_backupRoot);

        _pathResolverMock = new Mock<ICanonicalPathResolver>();
        _storageMock = new Mock<IBackupStorage>();

        _storageMock.Setup(s => s.IsPathWithinControlledRoot(It.IsAny<string>())).Returns(true);
        _storageMock.Setup(s => s.GetBackupRoot()).Returns(_backupRoot);

        _provider = new WindowsFileBackupProvider(_pathResolverMock.Object, _storageMock.Object);
    }

    [Fact]
    public async Task BackupFileSystemArtifactAsync_ShouldBackupSingleFile_AndComputeHash()
    {
        var testFile = Path.Combine(_testTempRoot, "test.txt");
        File.WriteAllText(testFile, "Hello World");

        var item = new CleanupPlanItem { ArtifactType = ArtifactType.File, Path = testFile };

        var backup = await _provider.BackupFileSystemArtifactAsync(item, _backupRoot, default);

        backup.Status.Should().Be(BackupStatus.Verifying);
        backup.Size.Should().Be(11);
        backup.Hash.Should().NotBeNullOrEmpty();
        File.Exists(backup.BackupPath).Should().BeTrue();
        
        // Original untouched
        File.Exists(testFile).Should().BeTrue();
    }

    [Fact]
    public async Task BackupFileSystemArtifactAsync_ShouldBackupDirectory_AndManifest()
    {
        var sourceDir = Path.Combine(_testTempRoot, "AppDir");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "Data1");
        
        var subDir = Path.Combine(sourceDir, "Sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file2.txt"), "Data2");

        var item = new CleanupPlanItem { ArtifactType = ArtifactType.Directory, Path = sourceDir };

        var backup = await _provider.BackupFileSystemArtifactAsync(item, _backupRoot, default);

        backup.Status.Should().Be(BackupStatus.Verifying);
        backup.Size.Should().Be(10); // Data1 (5) + Data2 (5)
        Directory.Exists(backup.BackupPath).Should().BeTrue();
        File.Exists(backup.BackupPath + "_manifest.json").Should().BeTrue();

        // Original untouched
        File.Exists(Path.Combine(sourceDir, "file1.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyFileSystemBackupAsync_ShouldReturnValid_WhenBackupIsIntact()
    {
        var testFile = Path.Combine(_testTempRoot, "test2.txt");
        File.WriteAllText(testFile, "Integration test");
        var item = new CleanupPlanItem { ArtifactType = ArtifactType.File, Path = testFile };
        
        var backup = await _provider.BackupFileSystemArtifactAsync(item, _backupRoot, default);
        var verify = await _provider.VerifyFileSystemBackupAsync(backup, default);

        verify.IsValid.Should().BeTrue();
        verify.Hash.Should().Be(backup.Hash);
    }

    [Fact]
    public async Task VerifyFileSystemBackupAsync_ShouldReturnInvalid_WhenBackupIsAltered()
    {
        var testFile = Path.Combine(_testTempRoot, "test3.txt");
        File.WriteAllText(testFile, "Integration test");
        var item = new CleanupPlanItem { ArtifactType = ArtifactType.File, Path = testFile };
        
        var backup = await _provider.BackupFileSystemArtifactAsync(item, _backupRoot, default);
        
        // Corrupt the backup
        File.WriteAllText(backup.BackupPath, "Corrupted");

        var verify = await _provider.VerifyFileSystemBackupAsync(backup, default);

        verify.IsValid.Should().BeFalse();
        verify.FailureReason.Should().Contain("Integrity check failed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempRoot))
        {
            Directory.Delete(_testTempRoot, true);
        }
    }
}
