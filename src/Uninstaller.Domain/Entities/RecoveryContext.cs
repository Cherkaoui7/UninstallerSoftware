using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class RecoveryContext
{
    public Guid RecoveryItemId { get; set; }
    public Guid BackupId { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public string OriginalCanonicalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string? ExpectedHash { get; set; }
    public string? ExpectedRegistryHive { get; set; }
    public string? ExpectedRegistryKeyPath { get; set; }
    public string? ExpectedShortcutTarget { get; set; }
    public BackupVerificationResult BackupVerificationResult { get; set; } = new BackupVerificationResult { IsValid = false };
    public DateTime RecoveryAuthorization { get; set; }
}
