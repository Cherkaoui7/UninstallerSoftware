using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class BackupServiceTests
{
    private readonly Mock<IBackupStorage> _storageMock;
    private readonly Mock<IFileBackupProvider> _fileBackupMock;
    private readonly Mock<IRegistryBackupProvider> _registryBackupMock;
    private readonly BackupService _backupService;

    public BackupServiceTests()
    {
        _storageMock = new Mock<IBackupStorage>();
        _fileBackupMock = new Mock<IFileBackupProvider>();
        _registryBackupMock = new Mock<IRegistryBackupProvider>();

        _storageMock.Setup(s => s.GetOrCreateSessionDirectory(It.IsAny<Guid>()))
            .Returns(@"C:\ProgramData\Uninstaller\Backups\Session123");

        _backupService = new BackupService(_storageMock.Object, _fileBackupMock.Object, _registryBackupMock.Object);
    }

    [Fact]
    public async Task CreateBackupManifestAsync_ShouldRouteToFileProvider_WhenArtifactIsFile()
    {
        var plan = new CleanupPlan
        {
            UninstallSessionId = Guid.NewGuid(),
            Items = new List<CleanupPlanItem>
            {
                new CleanupPlanItem { ArtifactType = ArtifactType.File, Path = @"C:\App\test.txt", Recommended = true }
            }
        };

        var expectedBackup = new Backup { ArtifactType = ArtifactType.File, Status = BackupStatus.Verifying };
        _fileBackupMock.Setup(f => f.BackupFileSystemArtifactAsync(It.IsAny<CleanupPlanItem>(), It.IsAny<string>(), default))
            .ReturnsAsync(expectedBackup);

        var manifest = await _backupService.CreateBackupManifestAsync(plan);

        manifest.Backups.Should().ContainSingle();
        manifest.Backups[0].Should().Be(expectedBackup);
        _fileBackupMock.Verify(f => f.BackupFileSystemArtifactAsync(plan.Items[0], @"C:\ProgramData\Uninstaller\Backups\Session123", default), Times.Once);
        _registryBackupMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBackupManifestAsync_ShouldRouteToRegistryProvider_WhenArtifactIsRegistryKey()
    {
        var plan = new CleanupPlan
        {
            UninstallSessionId = Guid.NewGuid(),
            Items = new List<CleanupPlanItem>
            {
                new CleanupPlanItem { ArtifactType = ArtifactType.RegistryKey, Path = @"HKLM\Software\MyApp", Recommended = true }
            }
        };

        var expectedBackup = new Backup { ArtifactType = ArtifactType.RegistryKey, Status = BackupStatus.Verifying };
        _registryBackupMock.Setup(r => r.BackupRegistryArtifactAsync(It.IsAny<CleanupPlanItem>(), It.IsAny<string>(), default))
            .ReturnsAsync(expectedBackup);

        var manifest = await _backupService.CreateBackupManifestAsync(plan);

        manifest.Backups.Should().ContainSingle();
        manifest.Backups[0].Should().Be(expectedBackup);
        _registryBackupMock.Verify(r => r.BackupRegistryArtifactAsync(plan.Items[0], @"C:\ProgramData\Uninstaller\Backups\Session123", default), Times.Once);
        _fileBackupMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyBackupManifestAsync_ShouldMarkVerified_WhenAllBackupsValid()
    {
        var manifest = new BackupManifest
        {
            Backups = new List<Backup>
            {
                new Backup { ArtifactType = ArtifactType.File, Status = BackupStatus.Verifying }
            }
        };

        _fileBackupMock.Setup(f => f.VerifyFileSystemBackupAsync(manifest.Backups[0], default))
            .ReturnsAsync(new BackupVerificationResult { IsValid = true });

        var result = await _backupService.VerifyBackupManifestAsync(manifest);

        result.IsValid.Should().BeTrue();
        manifest.Backups[0].Status.Should().Be(BackupStatus.Committed);
        manifest.Backups[0].VerificationStatus.Should().Be(BackupVerificationStatus.Verified);
    }

    [Fact]
    public async Task VerifyBackupManifestAsync_ShouldMarkFailed_WhenOneBackupInvalid()
    {
        var manifest = new BackupManifest
        {
            Backups = new List<Backup>
            {
                new Backup { ArtifactType = ArtifactType.File, Status = BackupStatus.Verifying },
                new Backup { ArtifactType = ArtifactType.RegistryKey, Status = BackupStatus.Verifying }
            }
        };

        _fileBackupMock.Setup(f => f.VerifyFileSystemBackupAsync(manifest.Backups[0], default))
            .ReturnsAsync(new BackupVerificationResult { IsValid = true });

        _registryBackupMock.Setup(r => r.VerifyRegistryBackupAsync(manifest.Backups[1], default))
            .ReturnsAsync(new BackupVerificationResult { IsValid = false, FailureReason = "Checksum mismatch" });

        var result = await _backupService.VerifyBackupManifestAsync(manifest);

        result.IsValid.Should().BeFalse();
        manifest.Backups[0].Status.Should().Be(BackupStatus.Committed);
        manifest.Backups[1].Status.Should().Be(BackupStatus.Failed);
        manifest.Backups[1].FailureReason.Should().Be("Checksum mismatch");
    }
}
