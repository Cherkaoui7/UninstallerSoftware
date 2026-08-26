using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class AuthorizedExecutionContext
{
    public Guid CleanupPlanItemId { get; set; }
    public string CanonicalPath { get; set; } = string.Empty;
    public ArtifactType ArtifactType { get; set; }
    
    // We assume there is some Result class for PreflightValidation.
    // For now, we will store a boolean or string representing it,
    // or just the ID of the authorization if we want to keep it simple.
    // Let's store the required metadata.
    public bool PreflightOutcomeAuthorized { get; set; }
    
    public Guid BackupId { get; set; }
    public BackupVerificationStatus BackupVerificationStatus { get; set; }
    
    public Guid ApplicationId { get; set; }
    public Guid ExecutionAuthorizationId { get; set; } = Guid.NewGuid();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Expected boundary root for final validation (filesystem)
    public string ExpectedRootPath { get; set; } = string.Empty;

    // Positive registry identity: the exact hive and key path authorized at preflight time.
    // The executor asserts these match the runtime-resolved path before any mutation.
    // Format for key:   HKCU (canonical short form)
    // Format for value: key path component only, with value name carried separately.
    public string ExpectedRegistryHive { get; set; } = string.Empty;
    public string ExpectedRegistryKeyPath { get; set; } = string.Empty;
}
