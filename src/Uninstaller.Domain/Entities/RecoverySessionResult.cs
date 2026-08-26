using System;
using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class RecoverySessionResult
{
    public Guid RecoverySessionId { get; set; }
    public RecoverySessionStatus Status { get; set; }
    public List<RecoveryResult> Results { get; set; } = new();
    
    public int TotalItems { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
