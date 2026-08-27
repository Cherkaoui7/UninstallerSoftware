using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.App.Services;

namespace Uninstaller.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public MainViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(INavigationService.CurrentViewModel))
            {
                OnPropertyChanged(nameof(CurrentViewModel));
            }
        };
        
        // Default route
        NavigateToDashboard();
    }

    public ObservableObject? CurrentViewModel => _navigationService.CurrentViewModel;

    [RelayCommand]
    private void NavigateToDashboard() => _navigationService.NavigateTo<DashboardViewModel>();

    [RelayCommand]
    private void NavigateToApplications() => _navigationService.NavigateTo<ApplicationsViewModel>();

    [RelayCommand]
    private void NavigateToHistory() => _navigationService.NavigateTo<HistoryViewModel>();

    [RelayCommand]
    private void NavigateToRecovery() => _navigationService.NavigateTo<RecoveryViewModel>();

    [RelayCommand]
    private void NavigateToSettings() => _navigationService.NavigateTo<SettingsViewModel>();
}
