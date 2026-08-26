using System;
using System.Threading.Tasks;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Abstractions;

public interface IItemExecutionTracker
{
    Task UpdateStateAsync(Guid itemId, CleanupItemExecutionState state);
}
