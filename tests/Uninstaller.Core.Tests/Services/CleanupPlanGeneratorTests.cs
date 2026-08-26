using System;
using System.Collections.Generic;
using System.Linq;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class CleanupPlanGeneratorTests
{
    private readonly CleanupPlanGenerator _generator = new CleanupPlanGenerator();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _appId = Guid.NewGuid();

    private ArtifactAnalysisResult CreateResult(
        string path, 
        ArtifactClassification classification, 
        int confidence, 
        bool isProtected, 
        List<Evidence>? evidence = null)
    {
        var artifact = new Artifact { Id = Guid.NewGuid(), Type = ArtifactType.Directory, Path = path };
        return new ArtifactAnalysisResult(
            artifact,
            confidence,
            classification,
            new List<string> { "Rule1" },
            evidence ?? new List<Evidence>(),
            new List<string>(),
            isProtected
        );
    }

    [Fact]
    public void Generate_EmptyInput_ProducesEmptyPlan()
    {
        var plan = _generator.Generate(_sessionId, _appId, new List<ArtifactAnalysisResult>());
        Assert.NotNull(plan);
        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.Summary.TotalArtifacts);
    }

    [Fact]
    public void Generate_ApplicationOwnedHighConfidence_IsRecommendedAndLowRisk()
    {
        var evidence = new List<Evidence> { new Evidence(EvidenceType.ExactInstallLocation, "Exact", "Scanner") };
        var result = CreateResult(@"C:\App", ArtifactClassification.ApplicationOwned, 95, false, evidence);
        var plan = _generator.Generate(_sessionId, _appId, new[] { result });

        var item = plan.Items.Single();
        Assert.True(item.Recommended);
        Assert.Equal(RiskLevel.Low, item.RiskLevel);
    }

    [Fact]
    public void Generate_ApplicationOwnedProtected_IsNotRecommendedAndBlocked()
    {
        var evidence = new List<Evidence> { new Evidence(EvidenceType.ExactInstallLocation, "Exact", "Scanner") };
        var result = CreateResult(@"C:\Users\User\Documents\App", ArtifactClassification.ApplicationOwned, 95, true, evidence);
        var plan = _generator.Generate(_sessionId, _appId, new[] { result });

        var item = plan.Items.Single();
        Assert.False(item.Recommended);
        Assert.Equal(RiskLevel.Blocked, item.RiskLevel);
        Assert.Contains(item.Reasons, r => r.Contains("Blocked"));
    }

    [Fact]
    public void Generate_UserData_IsNotRecommendedAndBlocked()
    {
        var result = CreateResult(@"C:\Users\User\Documents\App", ArtifactClassification.UserData, 40, true);
        var plan = _generator.Generate(_sessionId, _appId, new[] { result });

        var item = plan.Items.Single();
        Assert.False(item.Recommended);
        Assert.Equal(RiskLevel.Blocked, item.RiskLevel);
    }

    [Fact]
    public void Generate_SharedDependency_IsNotRecommendedAndHighRisk()
    {
        var result = CreateResult(@"C:\ProgramData\Vendor", ArtifactClassification.SharedDependency, 60, false);
        var plan = _generator.Generate(_sessionId, _appId, new[] { result });

        var item = plan.Items.Single();
        Assert.False(item.Recommended);
        Assert.Equal(RiskLevel.High, item.RiskLevel);
    }

    [Fact]
    public void Generate_Unknown_IsNotRecommendedAndHighRisk()
    {
        var result = CreateResult(@"C:\Random", ArtifactClassification.Unknown, 30, false);
        var plan = _generator.Generate(_sessionId, _appId, new[] { result });

        var item = plan.Items.Single();
        Assert.False(item.Recommended);
        Assert.Equal(RiskLevel.High, item.RiskLevel);
    }

    [Fact]
    public void Generate_DeterministicOrdering()
    {
        var res1 = CreateResult(@"C:\Z", ArtifactClassification.Unknown, 10, false);
        res1.Artifact.Type = ArtifactType.File; // Files sort after Directories (enum order typically, but let's just rely on Path)
        var res2 = CreateResult(@"C:\A", ArtifactClassification.Unknown, 10, false);
        res2.Artifact.Type = ArtifactType.Directory;

        var plan = _generator.Generate(_sessionId, _appId, new[] { res1, res2 });
        // File (0) comes before Directory (1) based on enum.
        Assert.Equal(@"C:\Z", plan.Items[0].Path);
        Assert.Equal(@"C:\A", plan.Items[1].Path);
    }

    [Fact]
    public void Generate_SummaryCounters()
    {
        var results = new[]
        {
            CreateResult(@"C:\App1", ArtifactClassification.ApplicationOwned, 95, false, new List<Evidence> { new Evidence(EvidenceType.ExactInstallLocation, "", "")}), // Recommended
            CreateResult(@"C:\App2", ArtifactClassification.UserData, 40, true), // Blocked
            CreateResult(@"C:\App3", ArtifactClassification.SharedDependency, 60, false) // High
        };

        var plan = _generator.Generate(_sessionId, _appId, results);

        Assert.Equal(3, plan.Summary.TotalArtifacts);
        Assert.Equal(1, plan.Summary.RecommendedItems);
        Assert.Equal(1, plan.Summary.BlockedItems);
        Assert.Equal(1, plan.Summary.UserDataItems);
        Assert.Equal(1, plan.Summary.SharedItems);
        Assert.Null(plan.Summary.EstimatedRecoverableSize);
    }

    [Fact]
    public void Generate_DuplicateArtifacts_AreFiltered()
    {
        var result = CreateResult(@"C:\App", ArtifactClassification.ApplicationOwned, 95, false);
        var duplicates = new[] { result, result, result };
        
        var plan = _generator.Generate(_sessionId, _appId, duplicates);
        
        Assert.Single(plan.Items);
        Assert.Equal(1, plan.Summary.TotalArtifacts);
    }
}
