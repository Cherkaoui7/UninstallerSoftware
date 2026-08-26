using Microsoft.Extensions.DependencyInjection;
using System;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class ExecutorResolver : IExecutorResolver
{
    private readonly IServiceProvider _serviceProvider;

    public ExecutorResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ICleanupExecutor? Resolve(ArtifactType artifactType)
    {
        return artifactType switch
        {
            ArtifactType.File or ArtifactType.Directory => _serviceProvider.GetService<IFileCleanupExecutor>(),
            ArtifactType.RegistryKey or ArtifactType.RegistryValue => _serviceProvider.GetService<IRegistryCleanupExecutor>(),
            ArtifactType.Shortcut => _serviceProvider.GetService<IShortcutCleanupExecutor>(),
            _ => null
        };
    }
}
