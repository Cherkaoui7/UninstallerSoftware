using System;
using Uninstaller.Core.Models.History;

namespace Uninstaller.App.ViewModels;

public class HistoryItemViewModel
{
    private readonly HistoryItemDetail _detail;

    public HistoryItemViewModel(HistoryItemDetail detail)
    {
        _detail = detail;
    }

    public Guid ItemId => _detail.ItemId;
    public string ArtifactType => _detail.ArtifactType;
    public string Path => _detail.Path;
    public string Classification => _detail.Classification;
    public int ConfidenceScore => _detail.ConfidenceScore;
    public string RiskLevel => _detail.RiskLevel;
    public string ExecutionState => _detail.ExecutionState;
    public string Outcome => _detail.Outcome;
    public string BackupStatus => _detail.BackupStatus;
    public string VerificationStatus => _detail.VerificationStatus;
    
    public string FailureReason
    {
        get
        {
            if (string.IsNullOrEmpty(_detail.FailureReason)) return string.Empty;
            
            // Map technical reasons to user-safe messages
            if (_detail.FailureReason.Contains("Access to the path", StringComparison.OrdinalIgnoreCase))
                return "Windows denied access to this operation.";
            if (_detail.FailureReason.Contains("Stale", StringComparison.OrdinalIgnoreCase))
                return "This cleanup plan was no longer current.";
            if (_detail.Outcome.Contains("Conflict", StringComparison.OrdinalIgnoreCase))
                return "The original location already contains data.";
                
            return _detail.FailureReason; // Default fallback if mapped
        }
    }
}
