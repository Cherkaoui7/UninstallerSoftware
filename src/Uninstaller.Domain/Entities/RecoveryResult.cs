using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class RecoveryResult
{
    public Guid RecoveryItemId { get; set; }
    public bool Success => Outcome == RecoveryOutcome.Recovered;
    public RecoveryOutcome Outcome { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}
