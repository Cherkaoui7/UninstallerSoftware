using System;
using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class CleanupPlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CleanupPlanId { get; set; }
    public Guid ArtifactId { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public string Path { get; set; } = string.Empty;
    public ArtifactClassification Classification { get; set; }
    public int ConfidenceScore { get; set; }
    public bool IsProtected { get; set; }
    public bool Recommended { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<string> AppliedRules { get; set; } = new();
    public List<Evidence> Evidence { get; set; } = new();
}
