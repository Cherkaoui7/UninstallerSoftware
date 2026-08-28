using System;

namespace Uninstaller.Core.Models.History;

public class HistoryActivity
{
    public Guid SessionId { get; set; }
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public ActivityType ActivityType { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
    public int TotalItems { get; set; }
    public int SuccessfulItems { get; set; }
    public int FailedItems { get; set; }
    public int WarningCount { get; set; }
}
