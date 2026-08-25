using System;

namespace Uninstaller.Domain.Entities;

public class Backup
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid ArtifactId { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string? Hash { get; set; }
    public DateTime CreatedAt { get; set; }
}
