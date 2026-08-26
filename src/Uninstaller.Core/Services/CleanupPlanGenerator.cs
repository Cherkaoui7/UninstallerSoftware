using System;
using System.Collections.Generic;
using System.Linq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class CleanupPlanGenerator : ICleanupPlanGenerator
{
    public CleanupPlan Generate(Guid sessionId, Guid applicationId, IEnumerable<ArtifactAnalysisResult> results)
    {
        var plan = new CleanupPlan
        {
            UninstallSessionId = sessionId,
            ApplicationId = applicationId,
            Status = CleanupPlanStatus.Generated
        };

        var orderedResults = results
            .GroupBy(r => r.Artifact.Id)
            .Select(g => g.First())
            .OrderBy(r => r.Artifact.Type)
            .ThenBy(r => NormalizePathForSorting(r.Artifact.Path))
            .ThenBy(r => r.Artifact.Id)
            .ToList();

        foreach (var result in orderedResults)
        {
            var item = CreateItem(plan.Id, result);
            plan.Items.Add(item);
        }

        plan.Summary = GenerateSummary(plan.Items);
        return plan;
    }

    private string NormalizePathForSorting(string path)
    {
        return path?.Replace("/", "\\").ToLowerInvariant() ?? string.Empty;
    }

    private CleanupPlanItem CreateItem(Guid planId, ArtifactAnalysisResult result)
    {
        var reasons = new List<string>();
        reasons.AddRange(result.AppliedRules);
        reasons.AddRange(result.Warnings);

        var item = new CleanupPlanItem
        {
            CleanupPlanId = planId,
            ArtifactId = result.Artifact.Id,
            ArtifactType = result.Artifact.Type,
            Path = result.Artifact.Path,
            Classification = result.Classification,
            ConfidenceScore = result.ConfidenceScore,
            IsProtected = result.IsProtected,
            Evidence = result.EvidenceUsed.ToList(),
            AppliedRules = result.AppliedRules.ToList()
        };

        // Determine Risk
        item.RiskLevel = CalculateRisk(result);
        reasons.Add($"Assigned risk level: {item.RiskLevel}");

        // Determine Recommendation
        item.Recommended = CalculateRecommendation(result, item.RiskLevel);
        reasons.Add(item.Recommended ? "Item is recommended for cleanup." : "Item is not recommended for cleanup.");

        item.Reasons = reasons;
        return item;
    }

    private RiskLevel CalculateRisk(ArtifactAnalysisResult result)
    {
        if (result.IsProtected) return RiskLevel.Blocked;
        if (result.Classification == ArtifactClassification.UserData) return RiskLevel.Blocked;
        if (result.Classification == ArtifactClassification.SharedDependency) return RiskLevel.High;
        if (result.Classification == ArtifactClassification.Unknown) return RiskLevel.High;

        // ApplicationOwned logic
        var hasExactInstall = result.EvidenceUsed.Any(e => e.Type == EvidenceType.ExactInstallLocation);
        if (hasExactInstall) return RiskLevel.Low;

        return RiskLevel.Medium;
    }

    private bool CalculateRecommendation(ArtifactAnalysisResult result, RiskLevel risk)
    {
        if (result.IsProtected) return false;
        if (result.Classification != ArtifactClassification.ApplicationOwned) return false;
        if (risk == RiskLevel.Blocked || risk == RiskLevel.High) return false;

        // Require high confidence
        if (result.ConfidenceScore >= 90) return true;

        return false;
    }

    private CleanupPlanSummary GenerateSummary(List<CleanupPlanItem> items)
    {
        return new CleanupPlanSummary
        {
            TotalArtifacts = items.Count,
            RecommendedItems = items.Count(i => i.Recommended),
            ProtectedItems = items.Count(i => i.IsProtected),
            UserDataItems = items.Count(i => i.Classification == ArtifactClassification.UserData),
            SharedItems = items.Count(i => i.Classification == ArtifactClassification.SharedDependency),
            UnknownItems = items.Count(i => i.Classification == ArtifactClassification.Unknown),
            BlockedItems = items.Count(i => i.RiskLevel == RiskLevel.Blocked),
            EstimatedRecoverableSize = null // Size calculation deferred to future iteration
        };
    }
}
