using System;

namespace Uninstaller.Domain.Entities;

public class BackupVerificationResult
{
    public bool IsValid { get; set; }
    public string? Hash { get; set; }
    public long? Size { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? VerifiedAt { get; set; }
}
