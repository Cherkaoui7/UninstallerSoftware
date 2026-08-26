using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Cleanup;

public class WindowsFileCleanupExecutor : IFileCleanupExecutor
{
    private readonly ICanonicalPathResolver _pathResolver;

    public WindowsFileCleanupExecutor(ICanonicalPathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public Task<CleanupExecutionResult> ExecuteAsync(AuthorizedExecutionContext context, CancellationToken cancellationToken = default)
    {
        var result = new CleanupExecutionResult
        {
            ItemId = context.CleanupPlanItemId,
            CanonicalPath = context.CanonicalPath,
            WasPreflightValidated = context.PreflightOutcomeAuthorized,
            WasBackupVerified = context.BackupVerificationStatus == BackupVerificationStatus.Verified,
            RequiresReboot = false
        };

        if (!result.WasPreflightValidated || !result.WasBackupVerified)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Missing authorization or verified backup.";
            return Task.FromResult(result);
        }

        // 4. Final Execution-Time Validation
        var safetyResult = _pathResolver.ResolveAndVerify(context.CanonicalPath, context.ExpectedRootPath, cancellationToken);
        result.WasFinalValidationPerformed = true;

        if (!safetyResult.IsValid)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = $"Final validation failed: {safetyResult.Reason}";
            return Task.FromResult(result);
        }

        if (safetyResult.IsProtected)
        {
            result.Outcome = CleanupOutcome.Protected;
            result.FailureReason = "Path is protected.";
            return Task.FromResult(result);
        }

        if (safetyResult.IsReparsePoint)
        {
            result.Outcome = CleanupOutcome.ReparseBlocked;
            result.FailureReason = "Path is a reparse point.";
            return Task.FromResult(result);
        }

        if (!string.IsNullOrEmpty(context.ExpectedRootPath) && !safetyResult.IsWithinExpectedRoot)
        {
            result.Outcome = CleanupOutcome.OutsideExpectedRoot;
            result.FailureReason = "Path is outside expected root.";
            return Task.FromResult(result);
        }

        if (!string.Equals(safetyResult.CanonicalPath, context.CanonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Canonical path changed between authorization and execution.";
            return Task.FromResult(result);
        }

        bool exists = context.ArtifactType == ArtifactType.Directory 
            ? Directory.Exists(context.CanonicalPath)
            : File.Exists(context.CanonicalPath);

        if (!exists)
        {
            result.Outcome = CleanupOutcome.NotFound;
            result.FailureReason = "Target artifact not found.";
            return Task.FromResult(result);
        }

        // 5 & 6. Execution
        try
        {
            if (context.ArtifactType == ArtifactType.File || context.ArtifactType == ArtifactType.Shortcut)
            {
                File.Delete(context.CanonicalPath);
            }
            else if (context.ArtifactType == ArtifactType.Directory)
            {
                // Strict rule: do not pass recursive=true
                Directory.Delete(context.CanonicalPath, false);
            }
            else
            {
                result.Outcome = CleanupOutcome.ValidationFailed;
                result.FailureReason = $"Unsupported artifact type for file executor: {context.ArtifactType}";
                return Task.FromResult(result);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Outcome = CleanupOutcome.AccessDenied;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (IOException ex)
        {
            // In .NET, sharing violations (locked file) and directory not empty both throw IOException.
            if (context.ArtifactType == ArtifactType.Directory)
            {
                result.Outcome = CleanupOutcome.DirectoryNotEmpty;
            }
            else
            {
                result.Outcome = CleanupOutcome.Locked;
            }
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            result.Outcome = CleanupOutcome.DeleteFailed;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }

        // 9. Post-Deletion Verification
        bool stillExists = context.ArtifactType == ArtifactType.Directory 
            ? Directory.Exists(context.CanonicalPath)
            : File.Exists(context.CanonicalPath);

        if (stillExists)
        {
            result.Outcome = CleanupOutcome.VerificationFailed;
            result.FailureReason = "File.Delete/Directory.Delete returned without exception, but artifact still exists.";
            return Task.FromResult(result);
        }

        result.Success = true;
        result.Outcome = CleanupOutcome.DeletedAndVerified;
        return Task.FromResult(result);
    }
}
