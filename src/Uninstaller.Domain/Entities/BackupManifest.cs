using System;
using System.Collections.Generic;

namespace Uninstaller.Domain.Entities;

public class BackupManifest
{
    public Guid SessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string ManifestVersion { get; set; } = "1.0";
    public List<Backup> Backups { get; set; } = new();
}
