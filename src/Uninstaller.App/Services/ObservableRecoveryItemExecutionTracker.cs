using System;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Enums;

namespace Uninstaller.App.Services;

public interface IObservableRecoveryItemExecutionTracker : IRecoveryItemExecutionTracker
{
    event EventHandler<RecoveryItemExecutionStateChangedEventArgs> StateChanged;
}

public class RecoveryItemExecutionStateChangedEventArgs : EventArgs
{
    public Guid ItemId { get; }
    public RecoveryItemExecutionState State { get; }

    public RecoveryItemExecutionStateChangedEventArgs(Guid itemId, RecoveryItemExecutionState state)
    {
        ItemId = itemId;
        State = state;
    }
}

public class ObservableRecoveryItemExecutionTracker : IObservableRecoveryItemExecutionTracker
{
    public event EventHandler<RecoveryItemExecutionStateChangedEventArgs>? StateChanged;

    public Task UpdateStateAsync(Guid itemId, RecoveryItemExecutionState state)
    {
        StateChanged?.Invoke(this, new RecoveryItemExecutionStateChangedEventArgs(itemId, state));
        return Task.CompletedTask;
    }
}
