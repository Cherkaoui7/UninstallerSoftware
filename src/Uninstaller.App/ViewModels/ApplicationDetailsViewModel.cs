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
    private readonly IUninstallSessionRepository _sessionRepository;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _cancellationTokenSource;

    public ApplicationDetailsViewModel(
        IUninstallService uninstallService,
        IResidualAnalysisService analysisService,
        IApplicationRepository repository,
        IUninstallSessionRepository sessionRepository,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _uninstallService = uninstallService;
        _analysisService = analysisService;
        _repository = repository;
        _sessionRepository = sessionRepository;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
        
        State = UIState.Ready;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeResidualsCommand))]
    private ApplicationViewModel? _application;

    public void LoadApplication(ApplicationViewModel app)
    {
        Application = app;
        State = UIState.Ready;
        UninstallCommand.NotifyCanExecuteChanged();
        AnalyzeResidualsCommand.NotifyCanExecuteChanged();
    }

    private bool CanUninstall() => Application != null && Application.IsPresent && State != UIState.Working && State != UIState.Loading;

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
                Application.IsPresent = false;
                Application.UninstallStatus = "Uninstalled";
                State = UIState.Success;
                StatusMessage = $"Successfully uninstalled {Application.Name}. You can now analyze residuals.";
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

            var latestSession = await _sessionRepository.GetLatestByApplicationIdAsync(appEntity.Id, _cancellationTokenSource.Token);
            if (latestSession == null || latestSession.Status != Uninstaller.Domain.Enums.UninstallSessionStatus.Completed)
            {
                SetError("Residual analysis requires a completed uninstall.");
                return;
            }

            var session = await _analysisService.RunAnalysisAsync(latestSession, appEntity, _cancellationTokenSource.Token);
            
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
