using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.Domain.Entities;
using AppEntity = Uninstaller.Domain.Entities.Application;

namespace Uninstaller.App.ViewModels;

public partial class RecoveryViewModel : ViewModelBase, IDisposable
{
    private readonly AppEntity _application;
    private readonly UninstallSession _session;
    private readonly INavigationService _navigationService;

    private readonly IRecoveryTransactionEngine _transactionEngine;
    private readonly IObservableRecoveryItemExecutionTracker _tracker;

    public RecoveryViewModel(
        AppEntity application,
        UninstallSession session,
        IEnumerable<Backup> backups,
        IRecoveryTransactionEngine transactionEngine,
        IObservableRecoveryItemExecutionTracker tracker,
        INavigationService navigationService,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _transactionEngine = transactionEngine ?? throw new ArgumentNullException(nameof(transactionEngine));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        Items = new ObservableCollection<RecoveryItemViewModel>(
            backups.Select(b => new RecoveryItemViewModel(b))
        );

        foreach (var item in Items)
        {
            item.PropertyChanged += Item_PropertyChanged;
        }

        UpdateSummary();
        State = Enums.UIState.Ready;
    }

    public ObservableCollection<RecoveryItemViewModel> Items { get; }
    
    public string ApplicationName => _application.Name;
    public string ApplicationVersion => _application.Version ?? "Unknown Version";
    public Guid CleanupSessionId => _session.Id;

    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private int _selectedItemsCount;

    [ObservableProperty]
    private int _unrecoverableItemsCount;

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecoveryItemViewModel.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        TotalItems = Items.Count;
        SelectedItemsCount = Items.Count(i => i.IsSelected);
        UnrecoverableItemsCount = Items.Count(i => !i.IsRecoverable);
        
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    public bool CanConfirm => SelectedItemsCount > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        var selectedBackups = Items
            .Where(i => i.IsSelected)
            .Select(i => i.Backup)
            .ToList();

        // Navigate to RecoverySessionViewModel
        _navigationService.NavigateTo(new RecoverySessionViewModel(
            _application,
            _session,
            selectedBackups,
            _transactionEngine,
            _tracker,
            _navigationService,
            ErrorBoundary
        ));
    }

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= Item_PropertyChanged;
        }
    }
}
