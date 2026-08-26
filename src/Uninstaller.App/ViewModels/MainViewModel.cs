using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Windows;
using System.Windows;

namespace Uninstaller.App.ViewModels;

public enum DiscoveryState
{
    Idle,
    Discovering,
    Completed,
    Failed,
    Cancelled
}

public partial class MainViewModel : ObservableObject
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IApplicationRepository _repository;
    private readonly ICommandParser _commandParser;
    private readonly IUninstallService _uninstallService;
    private CancellationTokenSource? _cancellationTokenSource;

    public MainViewModel(
        IDiscoveryService discoveryService, 
        IApplicationRepository repository,
        ICommandParser commandParser,
        IUninstallService uninstallService)
    {
        _discoveryService = discoveryService;
        _repository = repository;
        _commandParser = commandParser;
        _uninstallService = uninstallService;
        
        Applications = new ObservableCollection<ApplicationViewModel>();
        ApplicationsView = CollectionViewSource.GetDefaultView(Applications);
        ApplicationsView.Filter = FilterApplications;
        
        // Initialize state
        State = DiscoveryState.Idle;
    }

    public ObservableCollection<ApplicationViewModel> Applications { get; }
    
    public ICollectionView ApplicationsView { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private DiscoveryState _state;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private ApplicationViewModel? _selectedApplication;

    partial void OnSearchTextChanged(string value)
    {
        ApplicationsView.Refresh();
    }

    private bool FilterApplications(object obj)
    {
        if (obj is not ApplicationViewModel app) return false;
        if (!app.IsPresent) return false; // Hide historically uninstalled apps by default for now
        
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var search = SearchText.ToLowerInvariant();
        return (app.Name?.ToLowerInvariant().Contains(search) == true) ||
               (app.Publisher?.ToLowerInvariant().Contains(search) == true);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadApplicationsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load applications: {ex.Message}";
        }
    }

    private async Task LoadApplicationsAsync(CancellationToken cancellationToken)
    {
        var apps = await _repository.GetAllAsync(cancellationToken);
        
        Applications.Clear();
        foreach (var app in apps)
        {
            Applications.Add(new ApplicationViewModel(app));
        }
    }

    private bool CanScan() => State != DiscoveryState.Discovering;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        try
        {
            State = DiscoveryState.Discovering;
            StatusMessage = "Discovering applications...";
            ErrorMessage = string.Empty;
            
            _cancellationTokenSource = new CancellationTokenSource();
            
            var result = await _discoveryService.DiscoverApplicationsAsync(_cancellationTokenSource.Token);

            if (result.Cancelled)
            {
                State = DiscoveryState.Cancelled;
                StatusMessage = "Discovery cancelled by user.";
            }
            else if (result.Errors > 0)
            {
                State = DiscoveryState.Failed;
                StatusMessage = $"Discovery finished with {result.Errors} errors.";
                ErrorMessage = "Some applications may not have been discovered properly due to access errors.";
            }
            else
            {
                State = DiscoveryState.Completed;
                StatusMessage = $"Discovered {result.ApplicationsDiscovered} applications. Added: {result.ApplicationsAdded}, Updated: {result.ApplicationsUpdated}.";
            }

            // Reload grid
            await LoadApplicationsAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            State = DiscoveryState.Cancelled;
            StatusMessage = "Discovery cancelled.";
        }
        catch (Exception ex)
        {
            State = DiscoveryState.Failed;
            ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            StatusMessage = "Discovery failed.";
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanCancel() => State == DiscoveryState.Discovering && _cancellationTokenSource != null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            StatusMessage = "Cancelling...";
            _cancellationTokenSource.Cancel();
        }
    }

    private bool CanUninstall() => SelectedApplication != null && !SelectedApplication.IsUninstalling;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        var appVm = SelectedApplication;
        if (appVm == null) return;

        var app = await _repository.GetByIdAsync(appVm.Id, CancellationToken.None);
        if (app == null)
        {
            ErrorMessage = "Application details could not be found.";
            return;
        }

        var command = _commandParser.Parse(app);
        if (!command.IsValid)
        {
            ErrorMessage = "Cannot resolve a valid, safe uninstallation command for this application.";
            return;
        }

        var confirmationVm = new UninstallConfirmationViewModel(app, command);
        var window = new UninstallConfirmationWindow(confirmationVm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() != true)
        {
            StatusMessage = "Uninstall cancelled by user.";
            return;
        }

        appVm.IsUninstalling = true;
        appVm.UninstallStatus = "Executing uninstall...";
        StatusMessage = $"Uninstalling {appVm.Name}...";

        try
        {
            var session = await _uninstallService.RunUninstallAsync(app, CancellationToken.None);

            if (session.Status == Uninstaller.Domain.Enums.UninstallSessionStatus.Completed)
            {
                appVm.UninstallStatus = "Completed";
                StatusMessage = $"Successfully uninstalled {appVm.Name}.";
                // Optionally remove from view entirely or mark as not present
                appVm.UninstallStatus = "Verified Removed";
                ApplicationsView.Refresh();
            }
            else
            {
                appVm.UninstallStatus = $"Failed: {session.FailureReason}";
                ErrorMessage = $"Uninstall failed for {appVm.Name}: {session.FailureReason}";
            }
        }
        catch (Exception ex)
        {
            appVm.UninstallStatus = "Error executing uninstall";
            ErrorMessage = $"Error during uninstallation: {ex.Message}";
        }
        finally
        {
            appVm.IsUninstalling = false;
            UninstallCommand.NotifyCanExecuteChanged();
        }
    }
}
