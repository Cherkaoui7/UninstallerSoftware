using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Uninstaller.App.Services;

public class NavigationService : ObservableObject, INavigationService, IDisposable
{
    private readonly IServiceProvider _rootServiceProvider;
    private IServiceScope? _currentScope;
    private ObservableObject? _currentViewModel;

    public NavigationService(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider;
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
        var newScope = _rootServiceProvider.CreateScope();
        TViewModel vm;
        try
        {
            vm = newScope.ServiceProvider.GetRequiredService<TViewModel>();
        }
        catch
        {
            newScope.Dispose();
            throw;
        }

        var oldScope = _currentScope;
        _currentScope = newScope;
        CurrentViewModel = vm;

        try
        {
            oldScope?.Dispose();
        }
        catch
        {
            // Safe cleanup of previous navigation scope
        }

        return vm;
    }

    public void NavigateTo(ObservableObject viewModel)
    {
        var oldScope = _currentScope;
        _currentScope = null;
        CurrentViewModel = viewModel;

        try
        {
            oldScope?.Dispose();
        }
        catch
        {
            // Safe cleanup of previous navigation scope
        }
    }

    public void Dispose()
    {
        try
        {
            if (_currentViewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _currentScope?.Dispose();
            _currentScope = null;
            _currentViewModel = null;
        }
        catch
        {
            // Safe shutdown cleanup
        }
    }
}
