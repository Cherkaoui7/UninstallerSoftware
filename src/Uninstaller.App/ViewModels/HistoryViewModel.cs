using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;

namespace Uninstaller.App.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;
    private readonly IHistoryViewModelFactory _historyViewModelFactory;
    private readonly ILogger<HistoryViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<HistoryActivity> _activities = new();

    [ObservableProperty]
    private HistoryActivity? _selectedActivity;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public HistoryViewModel(
        IErrorBoundaryService errorBoundary, 
        IHistoryRepository historyRepository, 
        INavigationService navigationService,
        IHistoryViewModelFactory historyViewModelFactory,
        ILogger<HistoryViewModel>? logger = null) 
        : base(errorBoundary)
    {
        _logger = logger ?? NullLogger<HistoryViewModel>.Instance;
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        _historyViewModelFactory = historyViewModelFactory;
        _logger.LogInformation("[Navigation] HistoryViewModel constructed in fresh scope.");
        State = Enums.UIState.Idle;
        _ = InitializeAsync();
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
            _logger.LogInformation("[Navigation] HistoryViewModel loaded {Count} recent activities.", activities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Navigation] Failed to load history activities.");
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
            _navigationService.NavigateTo(_historyViewModelFactory.CreateApplicationHistoryViewModel(activity.ApplicationId, activity.ApplicationName));
        }
    }

    [RelayCommand]
    private void ViewSessionDetails(HistoryActivity activity)
    {
        if (activity == null) return;

        if (activity.ActivityType == ActivityType.Cleanup)
        {
            _navigationService.NavigateTo(_historyViewModelFactory.CreateCleanupSessionHistoryViewModel(activity.SessionId));
        }
        else if (activity.ActivityType == ActivityType.Recovery)
        {
            _navigationService.NavigateTo(_historyViewModelFactory.CreateRecoverySessionHistoryViewModel(activity.SessionId));
        }
        else
        {
            ViewApplicationTimeline(activity);
        }
    }
}
