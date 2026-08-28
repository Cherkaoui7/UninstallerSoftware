using System;

namespace Uninstaller.Core.Models.History;

public class TimelineEvent
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public ActivityType ActivityType { get; set; }
    public DateTime? Timestamp { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? RelatedSessionId { get; set; }
}
