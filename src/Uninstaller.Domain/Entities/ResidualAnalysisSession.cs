using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class ResidualAnalysisSession
{
    public Guid Id { get; set; }
    public Guid UninstallSessionId { get; set; }
    
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    public ResidualAnalysisStatus Status { get; set; }
    public int ArtifactCount { get; set; }
    public int ErrorCount { get; set; }
    public CleanupPlan? Plan { get; set; }
    public string? FailureReason { get; set; }
}
