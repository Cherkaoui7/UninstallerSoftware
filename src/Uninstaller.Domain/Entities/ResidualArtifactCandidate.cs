using System.Collections.Generic;

namespace Uninstaller.Domain.Entities;

public record ResidualArtifactCandidate(Artifact Artifact, IReadOnlyList<Evidence> Evidence, string Source);
