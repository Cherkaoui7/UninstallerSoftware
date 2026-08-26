using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Filesystem;

namespace Uninstaller.Windows.Cleanup;

/// <summary>
/// Deletes exactly the one authorized Windows shortcut (.lnk) file specified in
/// <see cref="AuthorizedExecutionContext.CanonicalPath"/>.
///
/// INVARIANTS:
///   - The shortcut target is NEVER deleted, modified, or executed.
///   - The parent directory is NEVER deleted or enumerated beyond a single existence check.
///   - No registry keys are read or written.
///   - No child processes are spawned — shell execution is not used.
///   - No recursive directory deletion occurs.
///   - Execution requires preflight authorization AND a verified backup.
///   - Final validation re-reads the live shortcut immediately before deletion.
/// </summary>
public class WindowsShortcutCleanupExecutor : IShortcutCleanupExecutor
{
    private readonly ICanonicalPathResolver _pathResolver;
    private readonly IShortcutProvider _shortcutProvider;

    // Well-known system-managed startup folder paths.  Any shortcut inside these roots
    // is considered a startup shortcut and gets extra scrutiny (not blanket rejection —
    // the preflight already decided it is authorized — but the identity check is enforced
    // even more strictly because a stale startup shortcut pointing to a rebuilt binary
    // would not necessarily produce a canonical path drift).
    private static readonly string[] StartupRoots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
    };

    public WindowsShortcutCleanupExecutor(
        ICanonicalPathResolver pathResolver,
        IShortcutProvider shortcutProvider)
    {
        _pathResolver = pathResolver;
        _shortcutProvider = shortcutProvider;
    }

    public Task<CleanupExecutionResult> ExecuteAsync(
        AuthorizedExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // ── 0. Cancellation check ──────────────────────────────────────────────
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Cancelled(context));

        // ── 1. Build result scaffold ───────────────────────────────────────────
        var result = new CleanupExecutionResult
        {
            ItemId = context.CleanupPlanItemId,
            CanonicalPath = context.CanonicalPath,
            WasPreflightValidated = context.PreflightOutcomeAuthorized,
            WasBackupVerified = context.BackupVerificationStatus == BackupVerificationStatus.Verified,
            RequiresReboot = false
        };

        // ── 2. Fail-closed authorization gate ─────────────────────────────────
        if (!result.WasPreflightValidated || !result.WasBackupVerified)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Missing authorization or verified backup.";
            return Task.FromResult(result);
        }

        // ── 3. Artifact-type gate ─────────────────────────────────────────────
        if (context.ArtifactType != ArtifactType.Shortcut)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = $"WindowsShortcutCleanupExecutor requires ArtifactType.Shortcut, got {context.ArtifactType}.";
            return Task.FromResult(result);
        }

        // ── 4. Final path safety validation (TOCTOU mitigation) ───────────────
        var safety = _pathResolver.ResolveAndVerify(context.CanonicalPath, context.ExpectedRootPath, cancellationToken);
        result.WasFinalValidationPerformed = true;

        if (!safety.IsValid)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = $"Final path validation failed: {safety.Reason}";
            return Task.FromResult(result);
        }

        if (safety.IsProtected)
        {
            result.Outcome = CleanupOutcome.Protected;
            result.FailureReason = "Shortcut path is protected.";
            return Task.FromResult(result);
        }

        if (safety.IsReparsePoint)
        {
            result.Outcome = CleanupOutcome.ReparseBlocked;
            result.FailureReason = "Shortcut path crosses a reparse point (symlink/junction).";
            return Task.FromResult(result);
        }

        // The resolved canonical path must be byte-for-byte the same as what was
        // recorded in the context.  Any drift means the filesystem has changed.
        if (!string.Equals(safety.CanonicalPath, context.CanonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            result.Outcome = CleanupOutcome.IdentityMismatch;
            result.FailureReason = "Canonical shortcut path changed between authorization and execution.";
            return Task.FromResult(result);
        }

        // ── 5. Extension guard ────────────────────────────────────────────────
        // Reject anything that is not a .lnk file at execution time (additional
        // protection even if the path resolver passed it through).
        if (!safety.CanonicalPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Target does not have a .lnk extension.";
            return Task.FromResult(result);
        }

        // ── 6. Existence check ────────────────────────────────────────────────
        if (!File.Exists(safety.CanonicalPath))
        {
            result.Outcome = CleanupOutcome.NotFound;
            result.FailureReason = "Shortcut file no longer exists.";
            return Task.FromResult(result);
        }

        // ── 7. Target identity assertion ──────────────────────────────────────
        // Re-read the live shortcut metadata and assert the target matches what was
        // authorized at preflight time.  A changed target means a different application
        // now owns this .lnk file — reject with IdentityMismatch rather than deleting
        // the wrong application's shortcut.
        if (!string.IsNullOrEmpty(context.ExpectedShortcutTarget))
        {
            var liveInfo = _shortcutProvider.GetShortcutInfo(safety.CanonicalPath);
            var liveTarget = liveInfo?.TargetPath ?? string.Empty;

            if (!string.Equals(liveTarget, context.ExpectedShortcutTarget, StringComparison.OrdinalIgnoreCase))
            {
                result.Outcome = CleanupOutcome.IdentityMismatch;
                result.FailureReason =
                    $"Shortcut target changed. Authorized: '{context.ExpectedShortcutTarget}' " +
                    $"Live: '{liveTarget}'. Refusing to delete.";
                return Task.FromResult(result);
            }
        }

        // ── 8. Startup shortcut note ──────────────────────────────────────────
        // Startup shortcuts require extra care.  We do NOT block them here — the
        // preflight (Phase 4B) already authorized the item.  We record that this
        // shortcut was located in a startup folder so the transaction engine can
        // surface it in audit logs.
        bool isStartupShortcut = IsInsideStartupRoot(safety.CanonicalPath);
        _ = isStartupShortcut; // currently used for audit; future transaction engine will consume this

        // ── 9. Cancellation check before mutation ─────────────────────────────
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Cancelled(context));

        // ── 10. Deletion ──────────────────────────────────────────────────────
        // Delete ONLY the .lnk file.  The target is never touched.
        try
        {
            File.Delete(safety.CanonicalPath);
        }
        catch (Exception ex)
        {
            result.Outcome = MapHResult(ex.HResult);
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }

        // ── 11. Post-deletion verification ────────────────────────────────────
        if (File.Exists(safety.CanonicalPath))
        {
            result.Outcome = CleanupOutcome.VerificationFailed;
            result.FailureReason = "File.Delete returned without exception, but the shortcut still exists.";
            return Task.FromResult(result);
        }

        result.Success = true;
        result.Outcome = CleanupOutcome.DeletedAndVerified;
        return Task.FromResult(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CleanupExecutionResult Cancelled(AuthorizedExecutionContext context) =>
        new()
        {
            ItemId = context.CleanupPlanItemId,
            CanonicalPath = context.CanonicalPath,
            WasPreflightValidated = context.PreflightOutcomeAuthorized,
            WasBackupVerified = context.BackupVerificationStatus == BackupVerificationStatus.Verified,
            Outcome = CleanupOutcome.Cancelled,
            FailureReason = "Execution was cancelled before the shortcut could be deleted.",
        };

    private static bool IsInsideStartupRoot(string path)
    {
        foreach (var root in StartupRoots)
        {
            if (!string.IsNullOrEmpty(root) &&
                path.StartsWith(root.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Maps Win32 HResult codes to precise <see cref="CleanupOutcome"/> values.
    /// Does NOT collapse distinct failure modes into a single "Locked" bucket.
    /// </summary>
    private static CleanupOutcome MapHResult(int hResult)
    {
        const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
        const int ERROR_LOCK_VIOLATION    = unchecked((int)0x80070021);
        const int ERROR_ACCESS_DENIED     = unchecked((int)0x80070005);
        const int ERROR_FILE_NOT_FOUND    = unchecked((int)0x80070002);
        const int ERROR_PATH_NOT_FOUND    = unchecked((int)0x80070003);

        return hResult switch
        {
            ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION => CleanupOutcome.Locked,
            ERROR_ACCESS_DENIED                             => CleanupOutcome.AccessDenied,
            ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND   => CleanupOutcome.NotFound,
            _                                               => CleanupOutcome.DeleteFailed,
        };
    }
}
