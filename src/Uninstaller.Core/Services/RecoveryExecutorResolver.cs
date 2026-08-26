using System;
using Microsoft.Extensions.DependencyInjection;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class RecoveryExecutorResolver : IRecoveryExecutorResolver
{
    private readonly IServiceProvider _serviceProvider;

    public RecoveryExecutorResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IRecoveryExecutor? Resolve(ArtifactType artifactType)
    {
        return artifactType switch
        {
            ArtifactType.File or ArtifactType.Directory => _serviceProvider.GetService<IFileRecoveryExecutor>(),
            ArtifactType.RegistryKey or ArtifactType.RegistryValue => _serviceProvider.GetService<IRegistryRecoveryExecutor>(),
            ArtifactType.Shortcut => _serviceProvider.GetService<IShortcutRecoveryExecutor>(),
            _ => null
        };
    }
}
