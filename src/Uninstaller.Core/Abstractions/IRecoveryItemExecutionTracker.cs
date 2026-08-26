using System;
using System.Threading.Tasks;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Abstractions;

public interface IRecoveryItemExecutionTracker
{
    Task UpdateStateAsync(Guid itemId, RecoveryItemExecutionState state);
}
