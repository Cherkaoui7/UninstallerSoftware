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

public partial class RecoverySessionViewModel : ViewModelBase, IDisposable
{
    private readonly IRecoveryTransactionEngine _transactionEngine;
    private readonly IObservableRecoveryItemExecutionTracker _tracker;
    private readonly INavigationService _navigationService;
    private readonly AppEntity _application;
    private readonly UninstallSession _cleanupSession;
    private readonly List<Backup> _selectedBackups;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    
    // Maps RecoveryItem.Id back to the UI item
    private readonly Dictionary<Guid, RecoveryItemViewModel> _itemMap = new();

    public RecoverySessionViewModel(
        AppEntity application,
        UninstallSession cleanupSession,
        IEnumerable<Backup> selectedBackups,
        IRecoveryTransactionEngine transactionEngine,
        IObservableRecoveryItemExecutionTracker tracker,
        INavigationService navigationService,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _application = application;
        _cleanupSession = cleanupSession;
        _selectedBackups = selectedBackups.ToList();
        _transactionEngine = transactionEngine;
        _tracker = tracker;
        _navigationService = navigationService;

        Items = new ObservableCollection<RecoveryItemViewModel>();
        
        TotalCount = _selectedBackups.Count;
        State = UIState.Ready;
    }

    public ObservableCollection<RecoveryItemViewModel> Items { get; }

    public string ApplicationName => _application.Name;
    public Guid CleanupSessionId => _cleanupSession.Id;
    
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
    private int _conflictCount;

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

    [RelayCommand]
    private void ReturnToHistory()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
    }

    private bool _isExecutionCompleted;

    public async Task StartExecutionAsync()
    {
        if (State == UIState.Working || TotalCount == 0) return;

        SetWorking("Executing recovery plan...");
        StartedAt = DateTime.UtcNow;
        _isExecutionCompleted = false;

        _cts = new CancellationTokenSource();
        CancelCommand.NotifyCanExecuteChanged();

        _tracker.StateChanged += OnTrackerStateChanged;

        try
        {
            var recoveryItems = new List<RecoveryItem>();
            foreach (var backup in _selectedBackups)
            {
                var recItem = new RecoveryItem
                {
                    BackupArtifactId = backup.Id,
                    ArtifactType = backup.ArtifactType,
                    State = RecoveryItemExecutionState.Pending
                };
                recoveryItems.Add(recItem);
                
                var vm = new RecoveryItemViewModel(backup);
                _itemMap[recItem.Id] = vm;
                Items.Add(vm);
            }

            var recoverySession = new RecoverySession
            {
                ApplicationId = _application.Id,
                CleanupSessionId = _cleanupSession.Id,
                Items = recoveryItems
            };

            var result = await _transactionEngine.ExecuteAsync(recoverySession, _application, _cts.Token);
            
            ReconcileResult(result);
        }
        catch (Exception ex)
        {
            SetError(ErrorBoundary.HandleException(ex, "Executing Recovery"));
        }
        finally
        {
            _tracker.StateChanged -= OnTrackerStateChanged;
            _cts?.Dispose();
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnTrackerStateChanged(object? sender, RecoveryItemExecutionStateChangedEventArgs e)
    {
        // Marshal to UI thread
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_isExecutionCompleted) return;
            if (_itemMap.TryGetValue(e.ItemId, out var item))
            {
                item.State = e.State;
                UpdateCounters();
            }
        });
    }

    private void UpdateCounters()
    {
        SuccessCount = Items.Count(i => i.State == RecoveryItemExecutionState.Recovered);
        FailedCount = Items.Count(i => i.State == RecoveryItemExecutionState.Failed);
        ConflictCount = Items.Count(i => i.State == RecoveryItemExecutionState.Conflict);
        CancelledCount = Items.Count(i => i.State == RecoveryItemExecutionState.Cancelled);
        SkippedCount = 0; // Not explicitly tracked in UI state enum unless mapped from Cancelled/Failed

        CompletedCount = SuccessCount + FailedCount + ConflictCount + CancelledCount;
        
        if (TotalCount > 0)
        {
            ProgressPercentage = (CompletedCount / (double)TotalCount) * 100;
        }
    }

    private void ReconcileResult(RecoverySessionResult result)
    {
        _isExecutionCompleted = true;
        SuccessCount = result.SuccessCount;
        FailedCount = result.FailureCount;
        SkippedCount = result.SkippedCount;
        CompletedCount = result.TotalItems;
        ConflictCount = result.Results.Count(r => r.Outcome == RecoveryOutcome.RecoveryConflict);
        
        foreach(var execResult in result.Results)
        {
            if (_itemMap.TryGetValue(execResult.RecoveryItemId, out var item))
            {
                item.Outcome = execResult.Outcome;
                item.FailureReason = MapFailureReason(execResult.Outcome, execResult.FailureReason);
                
                // Ensure state matches final outcome
                if (execResult.Success) item.State = RecoveryItemExecutionState.Recovered;
                else if (execResult.Outcome == RecoveryOutcome.RecoveryConflict) item.State = RecoveryItemExecutionState.Conflict;
                else item.State = RecoveryItemExecutionState.Failed;
            }
        }
        
        UpdateCounters();

        switch (result.Status)
        {
            case RecoverySessionStatus.Completed:
                State = UIState.Success;
                StatusMessage = "Recovery completed successfully.";
                break;
            case RecoverySessionStatus.CompletedWithFailures:
                State = UIState.Warning;
                StatusMessage = "Recovery completed with failures or conflicts.";
                break;
            case RecoverySessionStatus.Cancelled:
                State = UIState.Cancelled;
                StatusMessage = "Recovery cancelled.";
                break;
            default:
                State = UIState.Error;
                StatusMessage = "Recovery failed.";
                break;
        }
    }

    private string? MapFailureReason(RecoveryOutcome outcome, string? reason)
    {
        switch (outcome)
        {
            case RecoveryOutcome.BackupInvalid:
                return "This backup could not be verified.";
            case RecoveryOutcome.RecoveryConflict:
                return "The original location already contains data, so this item was not restored.";
            case RecoveryOutcome.AccessDenied:
                return "Windows denied access to this destination.";
            case RecoveryOutcome.ValidationFailed:
                return "This destination is protected and cannot be restored automatically.";
            case RecoveryOutcome.VerificationFailed:
                return "The restored item could not be verified.";
        }
        return reason;
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
