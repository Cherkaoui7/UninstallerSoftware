using System;
using System.Collections.Generic;
using System.Linq;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class EvidenceEngine : IEvidenceEngine
{
    private static readonly string[] ProtectedPaths = 
    {
        @"Documents",
        @"Downloads",
        @"Desktop",
        @"Pictures",
        @"Videos",
        @"Music"
    };

    public ArtifactAnalysisResult Analyze(ResidualArtifactCandidate candidate)
    {
        var appliedRules = new List<string>();
        var warnings = new List<string>();
        int score = 0;
        ArtifactClassification classification = ArtifactClassification.Unknown;
        bool isProtected = false;

        // Extract factual evidence points
        bool hasPublisher = candidate.Evidence.Any(e => e.Type == EvidenceType.PublisherDirectoryMatch || e.Type == EvidenceType.ExactPublisherKeyMatch);
        bool hasAppName = candidate.Evidence.Any(e => e.Type == EvidenceType.ApplicationNameDirectoryMatch || e.Type == EvidenceType.ExactApplicationKeyMatch || e.Type == EvidenceType.ShortcutNameMatch);
        bool hasExactInstall = candidate.Evidence.Any(e => e.Type == EvidenceType.ExactInstallLocation);
        bool hasExactShortcutTarget = candidate.Evidence.Any(e => e.Type == EvidenceType.ExactShortcutTarget);
        bool hasLocationTargetMatch = candidate.Evidence.Any(e => e.Type == EvidenceType.InstallLocationTargetMatch);

        // Score Calculation (capped at 100)
        if (hasExactInstall)
        {
            score = 100;
            appliedRules.Add("Exact Install Location matches");
        }
        else if (hasExactShortcutTarget)
        {
            score = 100;
            appliedRules.Add("Shortcut Target exactly matches Install Location");
        }
        else if (hasLocationTargetMatch)
        {
            score = 90;
            appliedRules.Add("Target resides within Install Location");
        }
        else if (hasPublisher && hasAppName)
        {
            score = 90;
            appliedRules.Add("Publisher and Application Name matches");
        }
        else if (hasAppName)
        {
            score = 60;
            appliedRules.Add("Application Name matches");
        }
        else if (hasPublisher)
        {
            score = 30;
            appliedRules.Add("Publisher matches");
        }

        score = Math.Max(0, Math.Min(100, score));

        // Protection Rules
        if (candidate.Artifact.Path != null)
        {
            string normalizedPath = candidate.Artifact.Path.Replace("/", "\\").TrimEnd('\\');
            foreach (var protectedPath in ProtectedPaths)
            {
                if (normalizedPath.IndexOf($"\\{protectedPath}\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedPath.EndsWith($"\\{protectedPath}", StringComparison.OrdinalIgnoreCase))
                {
                    isProtected = true;
                    warnings.Add($"Path resides in protected user-data location: {protectedPath}");
                    appliedRules.Add("Protected User-Data Location Override");
                    break;
                }
            }
        }

        // Classification Rules (Strict precedence, decoupled from numerical score)
        if (isProtected)
        {
            classification = ArtifactClassification.UserData;
            warnings.Add("Path is protected user data location.");
        }
        else if (hasPublisher && !hasAppName)
        {
            classification = ArtifactClassification.SharedDependency; // Multi-app publisher root
            warnings.Add("Publisher root matched without specific application; treating as SharedDependency.");
        }
        else if (hasExactInstall || hasExactShortcutTarget || hasLocationTargetMatch || (hasPublisher && hasAppName))
        {
            classification = ArtifactClassification.ApplicationOwned;
        }
        else
        {
            classification = ArtifactClassification.Unknown;
        }

        return new ArtifactAnalysisResult(
            candidate.Artifact,
            score,
            classification,
            appliedRules,
            candidate.Evidence,
            warnings,
            isProtected
        );
    }
}
