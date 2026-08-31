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
using Microsoft.Extensions.DependencyInjection;

namespace Uninstaller.App.ViewModels;

public partial class RecoverySessionHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IHistoryRepository _historyRepository;
    private readonly INavigationService _navigationService;
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
