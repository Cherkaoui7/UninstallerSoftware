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

public partial class RecoverySessionHistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;

    public Guid SessionId { get; }

    [ObservableProperty]
    private HistoryActivity? _sessionDetails;

    [ObservableProperty]
    private ObservableCollection<HistoryItemViewModel> _items = new();

    public RecoverySessionHistoryViewModel(
        IErrorBoundaryService errorBoundary, 
        IHistoryRepository historyRepository, 
        INavigationService navigationService, 
        Guid sessionId) 
        : base(errorBoundary)
    {
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        SessionId = sessionId;
        State = Enums.UIState.Idle;
    }

    public async Task InitializeAsync()
    {
        SetWorking("Loading recovery details...");
        try
        {
            SessionDetails = await _historyRepository.GetRecoverySessionDetailsAsync(SessionId);
            var items = await _historyRepository.GetSessionItemDetailsAsync(SessionId, ActivityType.Recovery);
            Items = new ObservableCollection<HistoryItemViewModel>(items.Select(i => new HistoryItemViewModel(i)));
            State = Enums.UIState.Ready;
            StatusMessage = "Recovery details loaded.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load recovery details: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
    }
}
