using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.App.Enums;

namespace Uninstaller.App.ViewModels;

public partial class ApplicationsViewModel : ViewModelBase
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IApplicationRepository _repository;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _cancellationTokenSource;

    public ApplicationsViewModel(
        IDiscoveryService discoveryService, 
        IApplicationRepository repository,
        INavigationService navigationService,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _discoveryService = discoveryService;
        _repository = repository;
        _navigationService = navigationService;
        
        Applications = new ObservableCollection<ApplicationViewModel>();
        ApplicationsView = CollectionViewSource.GetDefaultView(Applications);
        ApplicationsView.Filter = FilterApplications;
        
        State = UIState.Ready;
        _ = InitializeAsync();
    }

    public ObservableCollection<ApplicationViewModel> Applications { get; }
    
    public ICollectionView ApplicationsView { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ViewDetailsCommand))]
    private ApplicationViewModel? _selectedApplication;

    partial void OnSearchTextChanged(string value)
    {
        ApplicationsView.Refresh();
    }

    private bool FilterApplications(object obj)
    {
        if (obj is not ApplicationViewModel app) return false;
        if (!app.IsPresent) return false;
        
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var search = SearchText.ToLowerInvariant();
        return (app.Name?.ToLowerInvariant().Contains(search) == true) ||
               (app.Publisher?.ToLowerInvariant().Contains(search) == true);
    }

    public async Task InitializeAsync()
    {
        try
        {
            SetWorking("Loading applications...");
            var apps = await _repository.GetAllAsync(CancellationToken.None);
            
            Applications.Clear();
            foreach (var app in apps)
            {
                Applications.Add(new ApplicationViewModel(app));
            }
            State = UIState.Ready;
            StatusMessage = $"Loaded {Applications.Count} applications.";
        }
        catch (Exception ex)
        {
            SetError(ErrorBoundary.HandleException(ex, "Loading Applications"));
        }
    }

    private bool CanScan() => State != UIState.Loading && State != UIState.Working;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        try
        {
            SetWorking("Discovering applications...");
            
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            
            var result = await _discoveryService.DiscoverApplicationsAsync(_cancellationTokenSource.Token);

            if (result.Cancelled)
            {
                State = UIState.Cancelled;
                StatusMessage = "Discovery cancelled by user.";
            }
            else if (result.Errors > 0)
            {
                SetWarning($"Discovery finished with {result.Errors} access errors.");
            }
            else
            {
                State = UIState.Success;
                StatusMessage = $"Discovered {result.ApplicationsDiscovered} applications. Added: {result.ApplicationsAdded}, Updated: {result.ApplicationsUpdated}.";
            }

            // Reload grid
            var apps = await _repository.GetAllAsync(CancellationToken.None);
            Applications.Clear();
            foreach (var app in apps)
            {
                Applications.Add(new ApplicationViewModel(app));
            }
        }
        catch (OperationCanceledException)
        {
            State = UIState.Cancelled;
            StatusMessage = "Discovery cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ErrorBoundary.HandleException(ex, "Scanning Applications"));
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            ScanCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancel() => (State == UIState.Loading || State == UIState.Working) && _cancellationTokenSource != null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            StatusMessage = "Cancelling...";
            _cancellationTokenSource.Cancel();
        }
    }

    private bool CanViewDetails() => SelectedApplication != null;

    [RelayCommand(CanExecute = nameof(CanViewDetails))]
    private void ViewDetails()
    {
        if (SelectedApplication != null)
        {
            var detailsVm = _navigationService.NavigateTo<ApplicationDetailsViewModel>();
            detailsVm.LoadApplication(SelectedApplication);
        }
    }
}
