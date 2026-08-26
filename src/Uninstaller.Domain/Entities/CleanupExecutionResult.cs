using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class CleanupExecutionResult
{
    public Guid ItemId { get; set; }
    public bool Success { get; set; }
    public CleanupOutcome Outcome { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    
    public bool WasPreflightValidated { get; set; }
    public bool WasFinalValidationPerformed { get; set; }
    public bool WasBackupVerified { get; set; }
    
    public string CanonicalPath { get; set; } = string.Empty;
    
    // RequiresReboot must remain false in Phase 4D
    public bool RequiresReboot { get; set; }
}
