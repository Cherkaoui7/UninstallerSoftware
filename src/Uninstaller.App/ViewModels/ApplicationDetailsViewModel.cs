using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.Core.Abstractions;
using Uninstaller.App.Services;
using Uninstaller.App.Enums;

namespace Uninstaller.App.ViewModels;

public partial class ApplicationDetailsViewModel : ViewModelBase
{
    private readonly IUninstallService _uninstallService;
    private readonly IResidualAnalysisService _analysisService;
    private readonly IApplicationRepository _repository;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _cancellationTokenSource;

    public ApplicationDetailsViewModel(
        IUninstallService uninstallService,
        IResidualAnalysisService analysisService,
        IApplicationRepository repository,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _uninstallService = uninstallService;
        _analysisService = analysisService;
        _repository = repository;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
        
        State = UIState.Ready;
    }

    [ObservableProperty]
    private ApplicationViewModel? _application;

    public void LoadApplication(ApplicationViewModel app)
    {
        Application = app;
        State = UIState.Ready;
    }

    private bool CanUninstall() => Application != null && State != UIState.Working && State != UIState.Loading;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        if (Application == null) return;
        
        try
        {
            SetWorking($"Uninstalling {Application.Name}...");
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            var appEntity = await _repository.GetByIdAsync(Application.Id, CancellationToken.None);
            if (appEntity == null)
            {
                SetError("Application details could not be found in repository.");
                return;
            }

            var session = await _uninstallService.RunUninstallAsync(appEntity, _cancellationTokenSource.Token);

            if (session.Status == Uninstaller.Domain.Enums.UninstallSessionStatus.Completed)
            {
                State = UIState.Success;
                StatusMessage = $"Successfully uninstalled {Application.Name}.";
                
            }
            else
            {
                SetError($"Uninstall failed: {session.FailureReason}");
            }
        }
        catch (OperationCanceledException)
        {
            State = UIState.Cancelled;
            StatusMessage = "Uninstall cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ErrorBoundary.HandleException(ex, "Uninstalling Application"));
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            UninstallCommand.NotifyCanExecuteChanged();
            AnalyzeResidualsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanAnalyze() => Application != null && State != UIState.Working && State != UIState.Loading;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeResidualsAsync()
    {
        if (Application == null) return;

        try
        {
            SetWorking($"Analyzing residuals for {Application.Name}...");
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            var appEntity = await _repository.GetByIdAsync(Application.Id, CancellationToken.None);
            if (appEntity == null)
            {
                SetError("Application details could not be found.");
                return;
            }

            var session = await _analysisService.RunAnalysisAsync(new Uninstaller.Domain.Entities.UninstallSession(), appEntity, _cancellationTokenSource.Token);
            
            if (session.Plan != null)
            {
                State = UIState.Success;
                StatusMessage = $"Analysis complete. Found {session.Plan.Items.Count} potential residuals.";
                
                var cleanupPlanVm = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<CleanupPlanViewModel>(_serviceProvider, session.Plan, appEntity);
                _navigationService.NavigateTo(cleanupPlanVm);
            }
            else
            {
                SetError($"Analysis failed to generate a cleanup plan. Reason: {session.FailureReason}");
            }
        }
        catch (OperationCanceledException)
        {
            State = UIState.Cancelled;
            StatusMessage = "Analysis cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ErrorBoundary.HandleException(ex, "Analyzing Residuals"));
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            UninstallCommand.NotifyCanExecuteChanged();
            AnalyzeResidualsCommand.NotifyCanExecuteChanged();
        }
    }
}
