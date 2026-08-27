using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Uninstaller.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.App.ViewModels;

public partial class CleanupPlanViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly CleanupPlan _plan;
    private readonly Application _application;

    public CleanupPlanViewModel(
        CleanupPlan plan,
        Application application,
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        IErrorBoundaryService errorBoundary) : base(errorBoundary)
    {
        _plan = plan;
        _application = application;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;

        Items = new ObservableCollection<CleanupItemViewModel>(
            plan.Items.Select(i => 
            {
                var vm = new CleanupItemViewModel(i);
                vm.PropertyChanged += (s, e) => 
                {
                    if (e.PropertyName == nameof(CleanupItemViewModel.IsSelected))
                    {
                        UpdateSummaries();
                    }
                };
                return vm;
            }));

        UpdateSummaries();
        State = Enums.UIState.Ready;
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; }

    [ObservableProperty]
    private string _applicationName = string.Empty;

    [ObservableProperty]
    private string _applicationVersion = string.Empty;

    [ObservableProperty]
    private string _applicationPublisher = string.Empty;

    [ObservableProperty]
    private DateTime _scanTimestamp;

    [ObservableProperty]
    private int _totalArtifacts;

    [ObservableProperty]
    private int _selectedArtifacts;

    [ObservableProperty]
    private int _recommendedArtifacts;

    [ObservableProperty]
    private int _protectedArtifacts;

    [ObservableProperty]
    private int _userDataArtifacts;

    [ObservableProperty]
    private int _sharedDependencyArtifacts;

    [ObservableProperty]
    private int _unknownArtifacts;

    [ObservableProperty]
    private int _blockedArtifacts;

    [ObservableProperty]
    private int _warningsCount;

    [ObservableProperty]
    private string _overallRisk = string.Empty;

    [ObservableProperty]
    private CleanupItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isConfirmationVisible;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private bool _hasExecutableItems;

    private void UpdateSummaries()
    {
        ApplicationName = _application.Name ?? string.Empty;
        ApplicationVersion = _application.Version ?? string.Empty;
        ApplicationPublisher = _application.Publisher ?? string.Empty;
        ScanTimestamp = _plan.CreatedAt;

        TotalArtifacts = Items.Count;
        SelectedArtifacts = Items.Count(i => i.IsSelected);
        RecommendedArtifacts = Items.Count(i => i.Recommended);
        ProtectedArtifacts = Items.Count(i => i.IsProtected);
        
        UserDataArtifacts = Items.Count(i => i.Model.Classification == ArtifactClassification.UserData);
        SharedDependencyArtifacts = Items.Count(i => i.Model.Classification == ArtifactClassification.SharedDependency);
        UnknownArtifacts = Items.Count(i => i.Model.Classification == ArtifactClassification.Unknown);
        BlockedArtifacts = Items.Count(i => i.Model.RiskLevel == RiskLevel.Blocked);
        
        WarningsCount = _plan.Warnings.Count;
        
        HasItems = Items.Any();
        HasExecutableItems = Items.Any(i => i.CanSelect);

        // Derive overall risk from selected items
        var selectedRisk = Items.Where(i => i.IsSelected).Select(i => i.Model.RiskLevel).DefaultIfEmpty(RiskLevel.Low).Max();
        OverallRisk = selectedRisk.ToString();
    }

    [RelayCommand]
    public void ReviewCleanup()
    {
        if (SelectedArtifacts == 0)
        {
            StatusMessage = "No items selected for cleanup.";
            return;
        }

        IsConfirmationVisible = true;
    }

    [RelayCommand]
    public void CancelConfirmation()
    {
        IsConfirmationVisible = false;
    }

    [RelayCommand]
    public void ExecuteCleanup()
    {
        if (SelectedArtifacts == 0) return;

        IsConfirmationVisible = false;
        
        var selectedIds = Items.Where(i => i.IsSelected).Select(i => i.Id).ToList();

        var execVm = ActivatorUtilities.CreateInstance<CleanupExecutionViewModel>(
            _serviceProvider, _plan, _application, selectedIds);
            
        _navigationService.NavigateTo(execVm);
    }
}
