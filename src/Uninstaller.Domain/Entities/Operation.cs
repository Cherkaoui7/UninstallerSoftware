using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class Operation
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid ArtifactId { get; set; }
    public OperationType OperationType { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public Guid? BackupId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsReversible { get; set; }
}
