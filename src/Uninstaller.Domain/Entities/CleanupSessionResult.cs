using System;
using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class CleanupSessionResult
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public CleanupSessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public List<CleanupExecutionResult> Results { get; set; } = new();
}
