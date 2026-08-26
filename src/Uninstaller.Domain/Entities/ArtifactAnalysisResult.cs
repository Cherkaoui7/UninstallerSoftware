using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public record ArtifactAnalysisResult(
    Artifact Artifact,
    int ConfidenceScore,
    ArtifactClassification Classification,
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<Evidence> EvidenceUsed,
    IReadOnlyList<string> Warnings,
    bool IsProtected
);
