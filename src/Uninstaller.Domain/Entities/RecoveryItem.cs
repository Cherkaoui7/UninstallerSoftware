using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class RecoveryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecoverySessionId { get; set; }
    public Guid CleanupPlanItemId { get; set; }
    public Guid BackupArtifactId { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public RecoveryItemExecutionState State { get; set; }
}
