using System;

namespace Uninstaller.Core.Models.History;

public class HistoryItemDetail
{
    public Guid ItemId { get; set; }
    public Guid ArtifactId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public string ExecutionState { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string BackupStatus { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
}
