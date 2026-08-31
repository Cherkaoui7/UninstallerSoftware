using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Uninstaller.App.Services;

public class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private ObservableObject? _currentViewModel;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ObservableObject? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (_currentViewModel is IDisposable oldDisposable && !ReferenceEquals(_currentViewModel, value))
            {
                try
                {
                    oldDisposable.Dispose();
                }
                catch
                {
                    // Safe cleanup
                }
            }
            SetProperty(ref _currentViewModel, value);
        }
    }

    public TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        CurrentViewModel = vm;
        return vm;
    }

    public void NavigateTo(ObservableObject viewModel)
    {
        CurrentViewModel = viewModel;
    }
}
