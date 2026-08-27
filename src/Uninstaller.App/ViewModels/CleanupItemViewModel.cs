using System;
using System.Linq;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Uninstaller.App.ViewModels;

public partial class CleanupItemViewModel : ObservableObject
{
    public CleanupPlanItem Model { get; }

    public CleanupItemViewModel(CleanupPlanItem item)
    {
        Model = item;
        _isSelected = item.Recommended;
    }

    public Guid Id => Model.Id;
    public string Path => Model.Path;
    public string ArtifactType => Model.ArtifactType.ToString();
    public string Classification => Model.Classification.ToString();
    public int ConfidenceScore => Model.ConfidenceScore;
    public string RiskLevel => Model.RiskLevel.ToString();
    public bool Recommended => Model.Recommended;
    public bool IsProtected => Model.IsProtected;
    
    public string Reasons => string.Join(System.Environment.NewLine, Model.Reasons);
    public string AppliedRules => string.Join(System.Environment.NewLine, Model.AppliedRules);
    
    // Convert Evidence into a readable string for the UI Detail Panel
    public string Evidence => string.Join(System.Environment.NewLine, Model.Evidence.Select(e => $"{e.Type}: {e.Description} ({e.Source})"));

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !CanSelect)
                return; // Prevent selection of forbidden items

            SetProperty(ref _isSelected, value);
        }
    }

    public bool CanSelect =>
        !IsProtected &&
        Model.Classification != ArtifactClassification.UserData &&
        Model.Classification != ArtifactClassification.SharedDependency &&
        Model.Classification != ArtifactClassification.Unknown &&
        Model.RiskLevel != Uninstaller.Domain.Enums.RiskLevel.Blocked;
}
