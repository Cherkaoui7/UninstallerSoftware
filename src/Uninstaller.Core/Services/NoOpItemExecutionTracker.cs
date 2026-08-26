using System;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class NoOpItemExecutionTracker : IItemExecutionTracker
{
    public Task UpdateStateAsync(Guid itemId, CleanupItemExecutionState state)
    {
        return Task.CompletedTask;
    }
}
