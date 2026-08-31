using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;

namespace Uninstaller.App.ViewModels;

public partial class CleanupSessionHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;
    private readonly IServiceScope? _ownedScope;

    public Guid SessionId { get; }

    [ObservableProperty]
    private HistoryActivity? _sessionDetails;

    [ObservableProperty]
    private ObservableCollection<HistoryItemViewModel> _items = new();

    public CleanupSessionHistoryViewModel(
        IErrorBoundaryService errorBoundary, 
        IHistoryRepository historyRepository, 
        INavigationService navigationService, 
        Guid sessionId,
        IServiceScope? ownedScope = null) 
        : base(errorBoundary)
    {
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        _ownedScope = ownedScope;
        SessionId = sessionId;
        State = Enums.UIState.Idle;
    }

    public async Task InitializeAsync()
    {
        SetWorking("Loading cleanup details...");
        try
        {
            SessionDetails = await _historyRepository.GetCleanupSessionDetailsAsync(SessionId);
            var items = await _historyRepository.GetSessionItemDetailsAsync(SessionId, ActivityType.Cleanup);
            Items = new ObservableCollection<HistoryItemViewModel>(items.Select(i => new HistoryItemViewModel(i)));
            State = Enums.UIState.Ready;
            StatusMessage = "Cleanup details loaded.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load cleanup details: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
    }

    [RelayCommand]
    private void OpenRecovery()
    {
        _navigationService.NavigateTo(new RecoverySessionHistoryViewModel(ErrorBoundary, _historyRepository, _navigationService, SessionId));
    }

    public void Dispose()
    {
        try
        {
            _ownedScope?.Dispose();
        }
        catch
        {
            // Safe cleanup
        }
    }
}
