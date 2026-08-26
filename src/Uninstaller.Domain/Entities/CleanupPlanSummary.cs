namespace Uninstaller.Domain.Entities;

public record CleanupPlanSummary
{
    public int TotalArtifacts { get; init; }
    public int RecommendedItems { get; init; }
    public int ProtectedItems { get; init; }
    public int UserDataItems { get; init; }
    public int SharedItems { get; init; }
    public int UnknownItems { get; init; }
    public int BlockedItems { get; init; }
    public long? EstimatedRecoverableSize { get; init; }
}
