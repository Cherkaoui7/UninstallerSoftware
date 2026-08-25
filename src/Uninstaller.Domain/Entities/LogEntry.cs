using System;

namespace Uninstaller.Domain.Entities;

public class LogEntry
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? ArtifactId { get; set; }
    public string? OperationType { get; set; }
    public string? Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime Timestamp { get; set; }
}
