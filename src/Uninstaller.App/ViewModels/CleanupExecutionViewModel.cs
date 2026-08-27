using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.App.Enums;
using AppEntity = Uninstaller.Domain.Entities.Application;

namespace Uninstaller.App.ViewModels;

public partial class CleanupExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly ICleanupTransactionEngine _transactionEngine;
    private readonly IObservableItemExecutionTracker _tracker;
    private readonly INavigationService _navigationService;
    private readonly CleanupPlan _plan;
    private readonly AppEntity _application;
    private readonly List<Guid> _selectedItemIds;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    public CleanupExecutionViewModel(
        CleanupPlan plan,
        AppEntity application,
        IEnumerable<Guid> selectedItemIds,
        ICleanupTransactionEngine transactionEngine,
        IObservableItemExecutionTracker tracker,
        INavigationService navigationService,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _plan = plan;
        _application = application;
        _selectedItemIds = selectedItemIds.ToList();
        _transactionEngine = transactionEngine;
        _tracker = tracker;
        _navigationService = navigationService;

        Items = new ObservableCollection<CleanupItemExecutionViewModel>(
            _plan.Items
                .Where(i => _selectedItemIds.Contains(i.Id))
                .Select(i => new CleanupItemExecutionViewModel(i))
        );

        TotalCount = Items.Count;
        State = UIState.Ready;
    }

    public ObservableCollection<CleanupItemExecutionViewModel> Items { get; }

    public string ApplicationName => _application.Name;
    public Guid SessionId => _plan.UninstallSessionId;
    
    [ObservableProperty]
    private DateTime _startedAt;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _successCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private int _cancelledCount;

    [ObservableProperty]
    private double _progressPercentage;

    public bool CanCancel => State == UIState.Working && _cts != null && !_cts.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            StatusMessage = "Finishing current operation...";
            _cts.Cancel();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task StartExecutionAsync()
    {
        if (State == UIState.Working || TotalCount == 0) return;

        SetWorking("Executing cleanup plan...");
        StartedAt = DateTime.UtcNow;

        _cts = new CancellationTokenSource();
        CancelCommand.NotifyCanExecuteChanged();

        _tracker.StateChanged += OnTrackerStateChanged;

        try
        {
            var result = await _transactionEngine.ExecuteAsync(_plan, _application, _selectedItemIds, _cts.Token);
            
            ReconcileResult(result);
        }
        catch (Exception ex)
        {
            SetError(ErrorBoundary.HandleException(ex, "Executing Cleanup"));
        }
        finally
        {
            _tracker.StateChanged -= OnTrackerStateChanged;
            _cts?.Dispose();
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnTrackerStateChanged(object? sender, ItemExecutionStateChangedEventArgs e)
    {
        // Marshal to UI thread
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var item = Items.FirstOrDefault(i => i.Id == e.ItemId);
            if (item != null)
            {
                item.State = e.State;
                UpdateCounters();
            }
        });
    }

    private void UpdateCounters()
    {
        SuccessCount = Items.Count(i => i.State == CleanupItemExecutionState.Succeeded);
        FailedCount = Items.Count(i => i.State == CleanupItemExecutionState.Failed);
        SkippedCount = Items.Count(i => i.State == CleanupItemExecutionState.Skipped);
        CancelledCount = Items.Count(i => i.State == CleanupItemExecutionState.Cancelled);

        CompletedCount = SuccessCount + FailedCount + SkippedCount + CancelledCount;
        
        if (TotalCount > 0)
        {
            ProgressPercentage = (CompletedCount / (double)TotalCount) * 100;
        }
    }

    private void ReconcileResult(CleanupSessionResult result)
    {
        // Final state based on authoritative result
        SuccessCount = result.SuccessCount;
        FailedCount = result.FailureCount;
        SkippedCount = result.SkippedCount;
        CompletedCount = result.ProcessedCount;
        
        foreach(var execResult in result.Results)
        {
            var item = Items.FirstOrDefault(i => i.Id == execResult.ItemId);
            if (item != null)
            {
                item.Outcome = execResult.Outcome;
                item.FailureReason = MapFailureReason(execResult.FailureReason);
            }
        }

        switch (result.Status)
        {
            case CleanupSessionStatus.Completed:
                State = UIState.Success;
                StatusMessage = "Cleanup completed successfully.";
                break;
            case CleanupSessionStatus.CompletedWithFailures:
                State = UIState.Warning;
                StatusMessage = "Cleanup completed with failures.";
                break;
            case CleanupSessionStatus.Cancelled:
                State = UIState.Cancelled;
                StatusMessage = "Cleanup cancelled.";
                break;
            default:
                State = UIState.Error;
                StatusMessage = "Cleanup failed.";
                break;
        }
    }

    private string? MapFailureReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return null;
        
        if (reason.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) || reason.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase))
            return "Windows denied access to this item.";
            
        if (reason.Contains("stale", StringComparison.OrdinalIgnoreCase))
            return "This cleanup plan has changed. Please scan again.";
            
        if (reason.Contains("Reparse point", StringComparison.OrdinalIgnoreCase))
            return "This item is protected because its path changed unexpectedly.";
            
        if (reason.Contains("Backup", StringComparison.OrdinalIgnoreCase))
            return "The required backup could not be verified.";
            
        if (reason.Contains("verifi", StringComparison.OrdinalIgnoreCase))
            return "The cleanup result could not be verified.";

        return reason; // Fallback
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _tracker.StateChanged -= OnTrackerStateChanged;
            _cts?.Dispose();
            _isDisposed = true;
        }
    }
}
