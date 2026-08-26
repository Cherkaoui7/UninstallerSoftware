using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface IEvidenceEngine
{
    ArtifactAnalysisResult Analyze(ResidualArtifactCandidate candidate);
}
