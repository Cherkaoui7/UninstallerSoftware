using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class Backup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ArtifactId { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? Size { get; set; }
    public string? Hash { get; set; }
    public BackupStatus Status { get; set; }
    public BackupVerificationStatus VerificationStatus { get; set; }
    public string? FailureReason { get; set; }
}
