using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;

namespace Uninstaller.App.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ObservableCollection<HistoryActivity> _activities = new();

    [ObservableProperty]
    private HistoryActivity? _selectedActivity;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public HistoryViewModel(IErrorBoundaryService errorBoundary, IHistoryRepository historyRepository, INavigationService navigationService) 
        : base(errorBoundary)
    {
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        State = Enums.UIState.Idle;
    }

    public async Task InitializeAsync()
    {
        SetWorking("Loading history...");
        try
        {
            var activities = await _historyRepository.GetRecentActivitiesAsync();
            Activities = new ObservableCollection<HistoryActivity>(activities);
            State = Enums.UIState.Ready;
            StatusMessage = "History loaded.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load history: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Search()
    {
        // Search implementation if we want client side, else reload. 
        // We will just leave it open for now or reload.
    }

    [RelayCommand]
    private void ViewApplicationTimeline(HistoryActivity activity)
    {
        if (activity != null)
        {
            _navigationService.NavigateTo(new ApplicationHistoryViewModel(ErrorBoundary, _historyRepository, _navigationService, activity.ApplicationId, activity.ApplicationName));
        }
    }

    [RelayCommand]
    private void ViewSessionDetails(HistoryActivity activity)
    {
        if (activity == null) return;

        if (activity.ActivityType == ActivityType.Cleanup)
        {
            _navigationService.NavigateTo(new CleanupSessionHistoryViewModel(ErrorBoundary, _historyRepository, _navigationService, activity.SessionId));
        }
        else if (activity.ActivityType == ActivityType.Recovery)
        {
            _navigationService.NavigateTo(new RecoverySessionHistoryViewModel(ErrorBoundary, _historyRepository, _navigationService, activity.SessionId));
        }
        else
        {
            ViewApplicationTimeline(activity);
        }
    }
}
