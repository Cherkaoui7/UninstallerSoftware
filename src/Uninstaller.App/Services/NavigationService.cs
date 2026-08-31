using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Uninstaller.App.Services;

public class NavigationService : ObservableObject, INavigationService, IDisposable
{
    private readonly IServiceProvider _rootServiceProvider;
    private readonly ILogger<NavigationService> _logger;
    private IServiceScope? _currentScope;
    private ObservableObject? _currentViewModel;

    public NavigationService(
        IServiceProvider rootServiceProvider,
        ILogger<NavigationService>? logger = null)
    {
        _rootServiceProvider = rootServiceProvider;
        _logger = logger ?? NullLogger<NavigationService>.Instance;
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
        _logger.LogInformation("[Navigation] NavigationService.NavigateTo<{ViewModelType}> creating fresh scope.", typeof(TViewModel).Name);
        var newScope = _rootServiceProvider.CreateScope();
        TViewModel vm;
        try
        {
            vm = newScope.ServiceProvider.GetRequiredService<TViewModel>();
            _logger.LogInformation("[Navigation] Successfully resolved {ViewModelType} from fresh scope.", typeof(TViewModel).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Navigation] Failed to resolve {ViewModelType} from fresh scope.", typeof(TViewModel).Name);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Navigation] Safe cleanup of previous navigation scope encountered exception.");
        }

        return vm;
    }

    public void NavigateTo(ObservableObject viewModel)
    {
        _logger.LogInformation("[Navigation] NavigationService.NavigateTo(instance {ViewModelType}).", viewModel?.GetType().Name);
        var oldScope = _currentScope;
        _currentScope = null;
        CurrentViewModel = viewModel;

        try
        {
            oldScope?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Navigation] Safe cleanup of previous navigation scope encountered exception.");
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
