using System;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Enums;

namespace Uninstaller.App.Services;

public interface IObservableItemExecutionTracker : IItemExecutionTracker
{
    event EventHandler<ItemExecutionStateChangedEventArgs> StateChanged;
}

public class ItemExecutionStateChangedEventArgs : EventArgs
{
    public Guid ItemId { get; }
    public CleanupItemExecutionState State { get; }

    public ItemExecutionStateChangedEventArgs(Guid itemId, CleanupItemExecutionState state)
    {
        ItemId = itemId;
        State = state;
    }
}

public class ObservableItemExecutionTracker : IObservableItemExecutionTracker
{
    public event EventHandler<ItemExecutionStateChangedEventArgs>? StateChanged;

    public Task UpdateStateAsync(Guid itemId, CleanupItemExecutionState state)
    {
        StateChanged?.Invoke(this, new ItemExecutionStateChangedEventArgs(itemId, state));
        return Task.CompletedTask;
    }
}
