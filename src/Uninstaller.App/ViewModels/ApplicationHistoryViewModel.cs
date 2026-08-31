using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models.History;

namespace Uninstaller.App.ViewModels;

public partial class ApplicationHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;
    private readonly IHistoryViewModelFactory _historyViewModelFactory;
    private readonly ILogger<ApplicationHistoryViewModel> _logger;
    private readonly IServiceScope? _ownedScope;

    public Guid ApplicationId { get; }
    public string ApplicationName { get; }

    [ObservableProperty]
    private ObservableCollection<TimelineEvent> _timelineEvents = new();

    public ApplicationHistoryViewModel(
        IErrorBoundaryService errorBoundary, 
        IHistoryRepository historyRepository, 
        INavigationService navigationService, 
        IHistoryViewModelFactory historyViewModelFactory,
        Guid applicationId, 
        string applicationName,
        IServiceScope? ownedScope = null,
        ILogger<ApplicationHistoryViewModel>? logger = null) 
        : base(errorBoundary)
    {
        _logger = logger ?? NullLogger<ApplicationHistoryViewModel>.Instance;
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        _historyViewModelFactory = historyViewModelFactory;
        _ownedScope = ownedScope;
        ApplicationId = applicationId;
        ApplicationName = applicationName;
        _logger.LogInformation("[Navigation] ApplicationHistoryViewModel constructed for AppId={AppId}, AppName={AppName}", applicationId, applicationName);
        State = Enums.UIState.Idle;
        _ = InitializeAsync();
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
            _logger.LogInformation("[Navigation] ApplicationHistoryViewModel loaded {Count} timeline events for AppId={AppId}", events.Count, ApplicationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Navigation] Failed to load application timeline for AppId={AppId}", ApplicationId);
            SetError($"Failed to load timeline: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _logger.LogInformation("[Navigation] GoBack invoked from ApplicationHistoryViewModel, navigating back to HistoryViewModel");
        _navigationService.NavigateTo<HistoryViewModel>();
    }

    [RelayCommand]
    private void ViewEventDetails(TimelineEvent evt)
    {
        if (evt == null || evt.RelatedSessionId == null) return;

        _logger.LogInformation("[Navigation] ViewEventDetails invoked for Event={EventTitle}, RelatedSessionId={SessionId}, ActivityType={ActivityType}", 
            evt.Title, evt.RelatedSessionId, evt.ActivityType);

        if (evt.ActivityType == ActivityType.Cleanup)
        {
            var vm = _historyViewModelFactory.CreateCleanupSessionHistoryViewModel(evt.RelatedSessionId.Value);
            _navigationService.NavigateTo(vm);
        }
        else if (evt.ActivityType == ActivityType.Recovery)
        {
            var vm = _historyViewModelFactory.CreateRecoverySessionHistoryViewModel(evt.RelatedSessionId.Value);
            _navigationService.NavigateTo(vm);
        }
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
