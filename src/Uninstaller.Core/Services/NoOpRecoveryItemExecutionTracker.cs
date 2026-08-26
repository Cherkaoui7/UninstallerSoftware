using System;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class NoOpRecoveryItemExecutionTracker : IRecoveryItemExecutionTracker
{
    public Task UpdateStateAsync(Guid itemId, RecoveryItemExecutionState state)
    {
        return Task.CompletedTask;
    }
}
