using System;
using System.Collections.Generic;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Abstractions;

public interface ICleanupPlanGenerator
{
    CleanupPlan Generate(Guid sessionId, Guid applicationId, IEnumerable<ArtifactAnalysisResult> results);
}
