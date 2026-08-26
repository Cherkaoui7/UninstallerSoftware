using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class PreflightValidationResult
{
    public bool IsValid { get; set; }
    public bool IsAuthorized { get; set; }
    public PreflightValidationOutcome Outcome { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new List<string>();
    public string CanonicalPath { get; set; } = string.Empty;
    public bool IsProtected { get; set; }
    public bool IsReparsePoint { get; set; }
    public bool IsWithinExpectedRoot { get; set; }
    public bool ArtifactStillMatches { get; set; }
    public bool ApplicationStillMatches { get; set; }
    public bool PlanItemStillValid { get; set; }
    
    public string? ExpectedRootPath { get; set; }
    public string? ExpectedRegistryHive { get; set; }
    public string? ExpectedRegistryKeyPath { get; set; }
    public string? ExpectedShortcutTarget { get; set; }
}
