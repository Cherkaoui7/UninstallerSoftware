using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class UninstallSession
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public UninstallSessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public int? ExitCode { get; set; }
    public string? Strategy { get; set; }
    public string? FailureReason { get; set; }
    public int? ProcessId { get; set; }
    public VerificationResult VerificationResult { get; set; }
}
