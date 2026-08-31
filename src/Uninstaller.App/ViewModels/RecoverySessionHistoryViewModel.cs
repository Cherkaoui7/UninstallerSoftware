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

public partial class RecoverySessionHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;
    private readonly ILogger<RecoverySessionHistoryViewModel> _logger;
    private readonly IServiceScope? _ownedScope;

    public Guid SessionId { get; }

    [ObservableProperty]
    private HistoryActivity? _sessionDetails;

    [ObservableProperty]
    private ObservableCollection<HistoryItemViewModel> _items = new();

    public RecoverySessionHistoryViewModel(
        IErrorBoundaryService errorBoundary, 
        IHistoryRepository historyRepository, 
        INavigationService navigationService, 
        Guid sessionId,
        IServiceScope? ownedScope = null,
        ILogger<RecoverySessionHistoryViewModel>? logger = null) 
        : base(errorBoundary)
    {
        _logger = logger ?? NullLogger<RecoverySessionHistoryViewModel>.Instance;
        _historyRepository = historyRepository;
        _navigationService = navigationService;
        _ownedScope = ownedScope;
        SessionId = sessionId;
        _logger.LogInformation("[Navigation] RecoverySessionHistoryViewModel constructed for SessionId={SessionId}", sessionId);
        State = Enums.UIState.Idle;
        _ = InitializeAsync();
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
            _logger.LogInformation("[Navigation] RecoverySessionHistoryViewModel loaded details and {Count} items for SessionId={SessionId}", items.Count, SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Navigation] Failed to load recovery details for SessionId={SessionId}", SessionId);
            SetError($"Failed to load recovery details: {ex.Message}");
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _logger.LogInformation("[Navigation] GoBack invoked from RecoverySessionHistoryViewModel, navigating back to HistoryViewModel");
        _navigationService.NavigateTo<HistoryViewModel>();
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
