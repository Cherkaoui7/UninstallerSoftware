using System;
using System.Collections.ObjectModel;
using System.Linq;
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

public partial class CleanupSessionHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;
    private readonly IHistoryViewModelFactory _historyViewModelFactory;
    private readonly ILogger<CleanupSessionHistoryViewModel> _logger;
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
        IHistoryViewModelFactory historyViewModelFactory,
        Guid sessionId,
        IServiceScope? ownedScope = null,
        ILogger<CleanupSessionHistoryViewModel>? logger = null) 
        : base(errorBoundary)
    {
        _logger = logger ?? NullLogger<CleanupSessionHistoryViewModel>.Instance;
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        _historyViewModelFactory = historyViewModelFactory;
        _ownedScope = ownedScope;
        SessionId = sessionId;
        _logger.LogInformation("[Navigation] CleanupSessionHistoryViewModel constructed for SessionId={SessionId}", sessionId);
        State = Enums.UIState.Idle;
        _ = InitializeAsync();
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
            _logger.LogInformation("[Navigation] CleanupSessionHistoryViewModel loaded details and {Count} items for SessionId={SessionId}", items.Count, SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Navigation] Failed to load cleanup details for SessionId={SessionId}", SessionId);
            SetError($"Failed to load cleanup details: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _logger.LogInformation("[Navigation] GoBack invoked from CleanupSessionHistoryViewModel, navigating back to HistoryViewModel");
        _navigationService.NavigateTo<HistoryViewModel>();
    }

    [RelayCommand]
    private void OpenRecovery()
    {
        _logger.LogInformation("[Navigation] OpenRecovery invoked from CleanupSessionHistoryViewModel for SessionId={SessionId}", SessionId);
        var vm = _historyViewModelFactory.CreateRecoverySessionHistoryViewModel(SessionId);
        _navigationService.NavigateTo(vm);
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
