using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;

namespace Uninstaller.App.ViewModels;

public partial class ApplicationHistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;

    public Guid ApplicationId { get; }
    public string ApplicationName { get; }

    [ObservableProperty]
    private ObservableCollection<TimelineEvent> _timelineEvents = new();

    public ApplicationHistoryViewModel(
        IErrorBoundaryService errorBoundary, 
        IHistoryRepository historyRepository, 
        INavigationService navigationService, 
        Guid applicationId, 
        string applicationName) 
        : base(errorBoundary)
    {
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        ApplicationId = applicationId;
        ApplicationName = applicationName;
        State = Enums.UIState.Idle;
    }

    public async Task InitializeAsync()
    {
        SetWorking("Loading timeline...");
        try
        {
            var events = await _historyRepository.GetApplicationTimelineAsync(ApplicationId);
            TimelineEvents = new ObservableCollection<TimelineEvent>(events);
            State = Enums.UIState.Ready;
            StatusMessage = "Timeline loaded.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to load timeline: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
    }

    [RelayCommand]
    private void ViewEventDetails(TimelineEvent evt)
    {
        if (evt == null || evt.RelatedSessionId == null) return;

        if (evt.ActivityType == ActivityType.Cleanup)
        {
            _navigationService.NavigateTo(new CleanupSessionHistoryViewModel(ErrorBoundary, _historyRepository, _navigationService, evt.RelatedSessionId.Value));
        }
        else if (evt.ActivityType == ActivityType.Recovery)
        {
            _navigationService.NavigateTo(new RecoverySessionHistoryViewModel(ErrorBoundary, _historyRepository, _navigationService, evt.RelatedSessionId.Value));
        }
    }
}
