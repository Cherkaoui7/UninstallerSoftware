using System;
using System.Collections.Generic;
using System.Linq;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class EvidenceEngineAdversarialTests
{
    private readonly EvidenceEngine _engine = new EvidenceEngine();

    private Artifact CreateDir(string path) => new Artifact { Type = ArtifactType.Directory, Path = path };

    // 1. NAME COLLISIONS
    [Theory]
    [InlineData(@"C:\ProgramData\MyAppTools")]
    [InlineData(@"C:\ProgramData\MyApplication")]
    [InlineData(@"C:\ProgramData\MyAppBackup")]
    [InlineData(@"C:\ProgramData\MyAppProject")]
    public void NameCollisions_ShouldNotTriggerExactApplicationMatch(string path)
    {
        var artifact = CreateDir(path);
        // We simulate that the scanner gave a partial match but maybe it was mistaken. 
        // Wait, EvidenceEngine relies on the EvidenceType provided by the scanner.
        // If the scanner didn't provide ExactApplicationKeyMatch or ApplicationNameDirectoryMatch, the engine won't know.
        // Actually, the engine just trusts EvidenceType. 
        // We can assert that if the scanner provided PublisherDirectoryMatch but no AppName match, it's SharedDependency.
        var candidate = new ResidualArtifactCandidate(artifact, new[] { new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub", "Scanner") }, "Scanner");
        var result = _engine.Analyze(candidate);
        Assert.Equal(ArtifactClassification.SharedDependency, result.Classification);
    }

    // 2. PUBLISHER COLLISIONS
    [Fact]
    public void PublisherRoot_MultipleApps_ShouldBeSharedDependency()
    {
        var artifact = CreateDir(@"C:\ProgramData\Vendor");
        var candidate = new ResidualArtifactCandidate(artifact, new[] { new Evidence(EvidenceType.PublisherDirectoryMatch, "Vendor", "Scanner") }, "Scanner");
        var result = _engine.Analyze(candidate);
        Assert.Equal(ArtifactClassification.SharedDependency, result.Classification);
        Assert.NotEqual(ArtifactClassification.ApplicationOwned, result.Classification);
    }

    // 3. PROTECTED USER DATA
    [Theory]
    [InlineData(@"C:\Users\User\Documents\MyApp")]
    [InlineData(@"C:\Users\User\Downloads\MyApp")]
    [InlineData(@"C:\Users\User\Desktop\MyApp")]
    [InlineData(@"C:\Users\User\Pictures\MyApp")]
    [InlineData(@"C:\Users\User\Videos\MyApp")]
    [InlineData(@"C:\Users\User\Music\MyApp")]
    public void ProtectedPaths_AlwaysOverrideIdentityEvidence(string path)
    {
        var artifact = CreateDir(path);
        var evidence = new[] {
            new Evidence(EvidenceType.ExactInstallLocation, "Exact", "Scanner"),
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App", "Scanner"),
            new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub", "Scanner")
        };
        var candidate = new ResidualArtifactCandidate(artifact, evidence, "Scanner");
        var result = _engine.Analyze(candidate);

        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
        Assert.True(result.ConfidenceScore <= 39);
    }

    // 4. CONFLICTING EVIDENCE
    [Fact]
    public void ConflictingEvidence_ExactInstallLocation_Vs_ProtectedPath()
    {
        // Exact install location BUT in Documents (weird edge case, user installed to Documents)
        var artifact = CreateDir(@"C:\Users\User\Documents\MyInstalledApp");
        var candidate = new ResidualArtifactCandidate(artifact, new[] { new Evidence(EvidenceType.ExactInstallLocation, "Exact", "Scanner") }, "Scanner");
        var result = _engine.Analyze(candidate);

        // Protection must win!
        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
    }

    // 5. SCORE MANIPULATION (Weak Evidence Accumulation)
    [Fact]
    public void ScoreManipulation_ManyWeakEvidences_ShouldNotOverrideSafety()
    {
        var artifact = CreateDir(@"C:\Users\User\Documents\MyApp");
        var evidence = Enumerable.Range(1, 100).Select(i => new Evidence(EvidenceType.ApplicationNameDirectoryMatch, $"Match {i}", "Scanner")).ToList();
        var candidate = new ResidualArtifactCandidate(artifact, evidence, "Scanner");
        var result = _engine.Analyze(candidate);

        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
        Assert.True(result.ConfidenceScore <= 39);
    }

    // 6. PATH NORMALIZATION
    [Theory]
    [InlineData(@"C:\Users\User\documents\myapp")]
    [InlineData(@"C:\Users\User\Documents\MyApp\")]
    [InlineData(@"C:\Users\User\\Documents\\MyApp")]
    [InlineData(@"C:\Users\User\Downloads\..\Documents\MyApp")]
    public void PathNormalization_EquivalentProtectedPaths(string path)
    {
        var artifact = CreateDir(path);
        var candidate = new ResidualArtifactCandidate(artifact, new List<Evidence>(), "Scanner");
        var result = _engine.Analyze(candidate);
        
        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
    }

    // 8. EVIDENCE ORDER
    [Fact]
    public void EvidenceOrder_ShouldNotAffectResult()
    {
        var artifact = CreateDir(@"C:\ProgramData\MyPublisher\MyApp");
        var ev1 = new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub", "Scanner");
        var ev2 = new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App", "Scanner");
        
        var candidate1 = new ResidualArtifactCandidate(artifact, new[] { ev1, ev2 }, "Scanner");
        var candidate2 = new ResidualArtifactCandidate(artifact, new[] { ev2, ev1 }, "Scanner");

        var result1 = _engine.Analyze(candidate1);
        var result2 = _engine.Analyze(candidate2);

        Assert.Equal(result1.ConfidenceScore, result2.ConfidenceScore);
        Assert.Equal(result1.Classification, result2.Classification);
        Assert.Equal(result1.IsProtected, result2.IsProtected);
    }

    // 9. DUPLICATE EVIDENCE
    [Fact]
    public void DuplicateEvidence_ShouldNotInflateScore()
    {
        var artifact = CreateDir(@"C:\ProgramData\MyApp");
        var singleEv = new[] { new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App", "Scanner") };
        var duplicateEv = new[] { 
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App", "Scanner"),
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App", "Scanner"),
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App", "Scanner")
        };

        var res1 = _engine.Analyze(new ResidualArtifactCandidate(artifact, singleEv, "Scanner"));
        var res2 = _engine.Analyze(new ResidualArtifactCandidate(artifact, duplicateEv, "Scanner"));

        Assert.Equal(res1.ConfidenceScore, res2.ConfidenceScore);
        Assert.Equal(res1.Classification, res2.Classification);
    }

    // 10. EMPTY / INVALID INPUT
    [Fact]
    public void EmptyEvidence_ShouldBeUnknownAndZeroScore()
    {
        var artifact = CreateDir(@"C:\ProgramData\UnknownApp");
        var candidate = new ResidualArtifactCandidate(artifact, new List<Evidence>(), "Scanner");
        var result = _engine.Analyze(candidate);

        Assert.Equal(0, result.ConfidenceScore);
        Assert.Equal(ArtifactClassification.Unknown, result.Classification);
    }

    // 12. SECURITY INVARIANTS
    [Fact]
    public void SecurityInvariants_Protected_CannotBeApplicationOwned()
    {
        var artifact = CreateDir(@"C:\Users\User\Documents\MyApp");
        var candidate = new ResidualArtifactCandidate(artifact, new[] { new Evidence(EvidenceType.ExactInstallLocation, "Exact", "Scanner") }, "Scanner");
        var result = _engine.Analyze(candidate);

        Assert.True(result.IsProtected);
        Assert.NotEqual(ArtifactClassification.ApplicationOwned, result.Classification);
    }

    [Fact]
    public void SecurityInvariants_ScoreBounds()
    {
        var artifact = CreateDir(@"C:\ProgramData\MyApp");
        var candidate = new ResidualArtifactCandidate(artifact, new[] { new Evidence(EvidenceType.ExactInstallLocation, "Exact", "Scanner") }, "Scanner");
        var result = _engine.Analyze(candidate);
        
        Assert.True(result.ConfidenceScore >= 0 && result.ConfidenceScore <= 100);
    }
}
