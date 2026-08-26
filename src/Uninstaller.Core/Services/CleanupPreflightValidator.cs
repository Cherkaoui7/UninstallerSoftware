using System;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Core.Services;

public class CleanupPreflightValidator : ICleanupPreflightValidator
{
    private readonly ICanonicalPathResolver _pathResolver;
    private readonly IFileSystemService _fileSystem;
    private readonly IRegistryService _registry;
    private readonly IShortcutService _shortcut;

    public CleanupPreflightValidator(
        ICanonicalPathResolver pathResolver,
        IFileSystemService fileSystem,
        IRegistryService registry,
        IShortcutService shortcut)
    {
        _pathResolver = pathResolver;
        _fileSystem = fileSystem;
        _registry = registry;
        _shortcut = shortcut;
    }

    public Task<PreflightValidationResult> ValidateAsync(CleanupPlanItem item, Application application, CancellationToken cancellationToken = default)
    {
        var result = new PreflightValidationResult
        {
            IsValid = true,
            IsAuthorized = false,
            Outcome = PreflightValidationOutcome.Authorized,
            ArtifactStillMatches = true,
            ApplicationStillMatches = true,
            PlanItemStillValid = true
        };

        // 3. PLAN VALIDATION
        if (!item.Recommended)
        {
            return Task.FromResult(Reject(result, PreflightValidationOutcome.ValidationError, "Item is not Recommended"));
        }

        if (item.Classification != ArtifactClassification.ApplicationOwned)
        {
            return Task.FromResult(Reject(result, PreflightValidationOutcome.ValidationError, "Classification is not ApplicationOwned"));
        }

        if (item.IsProtected)
        {
            return Task.FromResult(Reject(result, PreflightValidationOutcome.Protected, "Artifact is marked as protected in the plan"));
        }

        if (item.RiskLevel == RiskLevel.High || item.RiskLevel == RiskLevel.Blocked)
        {
            return Task.FromResult(Reject(result, PreflightValidationOutcome.ValidationError, "RiskLevel is High or Blocked"));
        }

        // 4 & 5. PATH SECURITY & CURRENT STATE REVALIDATION
        switch (item.ArtifactType)
        {
            case ArtifactType.File:
            case ArtifactType.Directory:
                ValidateFileSystemArtifact(item, application, result);
                break;
            case ArtifactType.RegistryKey:
                ValidateRegistryArtifact(item, result);
                break;
            case ArtifactType.Shortcut:
                ValidateShortcutArtifact(item, application, result);
                break;
            default:
                return Task.FromResult(Reject(result, PreflightValidationOutcome.UnsupportedArtifact, $"Unsupported artifact type: {item.ArtifactType}"));
        }

        if (result.Outcome == PreflightValidationOutcome.Authorized)
        {
            result.IsAuthorized = true;
        }

        return Task.FromResult(result);
    }

    private void ValidateFileSystemArtifact(CleanupPlanItem item, Application application, PreflightValidationResult result)
    {
        string expectedRoot = null;
        if (!string.IsNullOrWhiteSpace(application.InstallLocation) && 
            _pathResolver.IsPathContainedWithin(item.Path, application.InstallLocation))
        {
            expectedRoot = application.InstallLocation;
        }

        var safety = _pathResolver.ResolveAndVerify(item.Path, expectedRoot);
        
        result.CanonicalPath = safety.CanonicalPath;
        result.IsProtected = safety.IsProtected;
        result.IsReparsePoint = safety.IsReparsePoint;
        result.IsWithinExpectedRoot = safety.IsWithinExpectedRoot;

        if (!safety.IsValid)
        {
            Reject(result, PreflightValidationOutcome.InvalidPath, $"Invalid path: {safety.Reason}");
            return;
        }

        if (safety.IsProtected)
        {
            Reject(result, PreflightValidationOutcome.Protected, "Path is protected by OS boundary policy.");
            return;
        }

        if (safety.IsReparsePoint)
        {
            Reject(result, PreflightValidationOutcome.ReparseBlocked, "Path crosses a reparse point (symlink/junction).");
            return;
        }

        if (expectedRoot != null && safety.IsWithinExpectedRoot)
        {
            var canonicalExpectedRoot = _pathResolver.ResolveAndVerify(expectedRoot).CanonicalPath;
            if (string.Equals(safety.CanonicalPath, canonicalExpectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                Reject(result, PreflightValidationOutcome.OutsideExpectedRoot, "Target is exactly the expected root, which is not allowed.");
                return;
            }
        }

        if (item.ArtifactType == ArtifactType.File)
        {
            if (!_fileSystem.FileExists(safety.CanonicalPath))
            {
                Reject(result, PreflightValidationOutcome.Missing, "File no longer exists.");
                return;
            }
        }
        else if (item.ArtifactType == ArtifactType.Directory)
        {
            if (!_fileSystem.DirectoryExists(safety.CanonicalPath))
            {
                Reject(result, PreflightValidationOutcome.Missing, "Directory no longer exists.");
                return;
            }
        }
    }

    private void ValidateRegistryArtifact(CleanupPlanItem item, PreflightValidationResult result)
    {
        var parts = item.Path.Split('\\', 2);
        if (parts.Length < 2)
        {
            Reject(result, PreflightValidationOutcome.InvalidPath, "Registry path is malformed.");
            return;
        }

        var root = parts[0];
        var path = parts[1];

        if (!_registry.KeyExists(root, path))
        {
            Reject(result, PreflightValidationOutcome.Missing, "Registry key no longer exists.");
            return;
        }

        result.CanonicalPath = item.Path;
    }

    private void ValidateShortcutArtifact(CleanupPlanItem item, Application application, PreflightValidationResult result)
    {
        var safety = _pathResolver.ResolveAndVerify(item.Path);
        
        result.CanonicalPath = safety.CanonicalPath;
        result.IsProtected = safety.IsProtected;
        result.IsReparsePoint = safety.IsReparsePoint;
        result.IsWithinExpectedRoot = safety.IsWithinExpectedRoot;

        if (!safety.IsValid)
        {
            Reject(result, PreflightValidationOutcome.InvalidPath, $"Invalid path: {safety.Reason}");
            return;
        }
        
        if (safety.IsProtected)
        {
            Reject(result, PreflightValidationOutcome.Protected, "Shortcut path is protected.");
            return;
        }

        if (safety.IsReparsePoint)
        {
            Reject(result, PreflightValidationOutcome.ReparseBlocked, "Shortcut path is a reparse point.");
            return;
        }

        if (!_shortcut.ShortcutExists(safety.CanonicalPath))
        {
            Reject(result, PreflightValidationOutcome.Missing, "Shortcut no longer exists.");
            return;
        }

        var target = _shortcut.GetShortcutTarget(safety.CanonicalPath);
        if (string.IsNullOrWhiteSpace(target))
        {
            Reject(result, PreflightValidationOutcome.StalePlan, "Shortcut target is empty or unreadable.");
            return;
        }

        var targetSafety = _pathResolver.ResolveAndVerify(target);
        if (targetSafety.IsProtected)
        {
            Reject(result, PreflightValidationOutcome.Protected, "Shortcut target is protected.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(application.InstallLocation) &&
            !_pathResolver.IsPathContainedWithin(target, application.InstallLocation))
        {
            Reject(result, PreflightValidationOutcome.IdentityMismatch, "Shortcut target no longer points to the application's install location.");
            return;
        }
    }

    private PreflightValidationResult Reject(PreflightValidationResult result, PreflightValidationOutcome outcome, string reason)
    {
        result.IsAuthorized = false;
        result.Outcome = outcome;
        result.FailureReason = reason;
        result.IsValid = false;
        
        if (outcome == PreflightValidationOutcome.IdentityMismatch) 
        {
            result.ApplicationStillMatches = false;
            result.ArtifactStillMatches = false;
        }
        
        if (outcome == PreflightValidationOutcome.Missing || outcome == PreflightValidationOutcome.StalePlan || outcome == PreflightValidationOutcome.ValidationError) 
        {
            result.PlanItemStillValid = false;
        }
        
        return result;
    }
}
