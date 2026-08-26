using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Abstractions;

public interface IExecutorResolver
{
    ICleanupExecutor? Resolve(ArtifactType artifactType);
}
