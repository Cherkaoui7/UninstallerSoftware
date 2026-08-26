using System;
using System.Collections.Generic;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Xunit;
using System.Linq;

namespace Uninstaller.Core.Tests.Services;

public class EvidenceEngineTests
{
    private EvidenceEngine _engine;
    private Artifact _artifactDoc;
    private Artifact _artifactProgramDataApp;
    private Artifact _artifactProgramDataPub;
    private Artifact _artifactRandom;

    public EvidenceEngineTests()
    {
        _engine = new EvidenceEngine();
        _artifactDoc = new Artifact { Type = ArtifactType.Directory, Path = @"C:\Users\User\Documents\MyApp" };
        _artifactProgramDataApp = new Artifact { Type = ArtifactType.Directory, Path = @"C:\ProgramData\MyPublisher\MyApp" };
        _artifactProgramDataPub = new Artifact { Type = ArtifactType.Directory, Path = @"C:\ProgramData\MyPublisher" };
        _artifactRandom = new Artifact { Type = ArtifactType.Directory, Path = @"C:\Random\MyApp" };
    }

    [Fact]
    public void Analyze_DocumentsProtected_CannotBecomeApplicationOwned()
    {
        // A. Documents\MyApp + exact name + publisher match + shortcut evidence
        var evidence = new List<Evidence>
        {
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "Name match", "Scanner"),
            new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub match", "Scanner"),
            new Evidence(EvidenceType.ShortcutNameMatch, "Shortcut match", "Scanner")
        };
        var candidate = new ResidualArtifactCandidate(_artifactDoc, evidence, "Scanner");

        var result = _engine.Analyze(candidate);

        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
        Assert.True(result.ConfidenceScore <= 39); // Cap
    }

    [Fact]
    public void Analyze_ProgramDataApp_StrongCandidate()
    {
        // B. ProgramData\MyPublisher\MyApp + exact publisher + exact application directory
        var evidence = new List<Evidence>
        {
            new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub match", "Scanner"),
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App match", "Scanner")
        };
        var candidate = new ResidualArtifactCandidate(_artifactProgramDataApp, evidence, "Scanner");

        var result = _engine.Analyze(candidate);

        Assert.False(result.IsProtected);
        Assert.Equal(ArtifactClassification.ApplicationOwned, result.Classification);
        Assert.Equal(90, result.ConfidenceScore);
    }

    [Fact]
    public void Analyze_ProgramDataPublisherOnly_SharedDependency()
    {
        // C. ProgramData\MyPublisher + publisher match + several applications
        var evidence = new List<Evidence>
        {
            new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub match", "Scanner")
        };
        var candidate = new ResidualArtifactCandidate(_artifactProgramDataPub, evidence, "Scanner");

        var result = _engine.Analyze(candidate);

        Assert.False(result.IsProtected);
        Assert.Equal(ArtifactClassification.SharedDependency, result.Classification);
        Assert.Equal(30, result.ConfidenceScore);
    }

    [Fact]
    public void Analyze_RandomDirAppName_NotHighConfidence()
    {
        // D. Random directory named MyApp + only name match
        var evidence = new List<Evidence>
        {
            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App match", "Scanner")
        };
        var candidate = new ResidualArtifactCandidate(_artifactRandom, evidence, "Scanner");

        var result = _engine.Analyze(candidate);

        Assert.False(result.IsProtected);
        Assert.Equal(ArtifactClassification.Unknown, result.Classification);
        Assert.Equal(60, result.ConfidenceScore);
    }

    [Fact]
    public void Analyze_IdenticalResult_WhenOrderChanged()
    {
        // E. Same evidence in 10 different orders -> identical result
        var evidence1 = new Evidence(EvidenceType.PublisherDirectoryMatch, "Pub match", "Scanner");
        var evidence2 = new Evidence(EvidenceType.ApplicationNameDirectoryMatch, "App match", "Scanner");

        var c1 = new ResidualArtifactCandidate(_artifactProgramDataApp, new List<Evidence> { evidence1, evidence2 }, "Scanner");
        var c2 = new ResidualArtifactCandidate(_artifactProgramDataApp, new List<Evidence> { evidence2, evidence1 }, "Scanner");

        var r1 = _engine.Analyze(c1);
        var r2 = _engine.Analyze(c2);

        Assert.Equal(r1.ConfidenceScore, r2.ConfidenceScore);
        Assert.Equal(r1.Classification, r2.Classification);
    }

    [Fact]
    public void Analyze_CasingVariations_IdenticalClassification()
    {
        // F. Path casing variations
        var artifactUpper = new Artifact { Type = ArtifactType.Directory, Path = @"C:\USERS\USER\DOCUMENTS\MYAPP" };
        var candidate = new ResidualArtifactCandidate(artifactUpper, new List<Evidence>(), "Scanner");
        
        var result = _engine.Analyze(candidate);
        
        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
    }

    [Fact]
    public void Analyze_TrailingSlash_IdenticalClassification()
    {
        // G. Trailing slash variations
        var artifactSlash = new Artifact { Type = ArtifactType.Directory, Path = @"C:\Users\User\Documents\MyApp\" };
        var candidate = new ResidualArtifactCandidate(artifactSlash, new List<Evidence>(), "Scanner");
        
        var result = _engine.Analyze(candidate);
        
        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
    }

    [Fact]
    public void Analyze_NormalizedPaths_Equivalent()
    {
        // H. Equivalent normalized paths using .. where appropriate
        var artifactDots = new Artifact { Type = ArtifactType.Directory, Path = @"C:\Users\User\Downloads\..\Documents\MyApp" };
        var candidate = new ResidualArtifactCandidate(artifactDots, new List<Evidence>(), "Scanner");
        
        var result = _engine.Analyze(candidate);
        
        Assert.True(result.IsProtected);
        Assert.Equal(ArtifactClassification.UserData, result.Classification);
    }
}
