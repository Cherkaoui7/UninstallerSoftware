using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Abstractions;

public interface IRecoveryExecutorResolver
{
    IRecoveryExecutor? Resolve(ArtifactType artifactType);
}
