using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Cleanup;

public class WindowsFileCleanupExecutor : IFileCleanupExecutor
{
    private readonly ICanonicalPathResolver _pathResolver;
    private readonly ILogger<WindowsFileCleanupExecutor> _logger;

    public WindowsFileCleanupExecutor(
        ICanonicalPathResolver pathResolver,
        ILogger<WindowsFileCleanupExecutor>? logger = null)
    {
        _pathResolver = pathResolver;
        _logger = logger ?? NullLogger<WindowsFileCleanupExecutor>.Instance;
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
            _logger.LogWarning("Execution rejected for item {ItemId}: Missing preflight authorization or verified backup.", context.CleanupPlanItemId);
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Missing authorization or verified backup.";
            return Task.FromResult(result);
        }

        // 4. Final Execution-Time Validation (TOCTOU Defense)
        var safetyResult = _pathResolver.ResolveAndVerify(context.CanonicalPath, context.ExpectedRootPath, cancellationToken);
        result.WasFinalValidationPerformed = true;

        if (!safetyResult.IsValid)
        {
            _logger.LogWarning("Final validation invalid for item {ItemId} on path {Path}: {Reason}", context.CleanupPlanItemId, context.CanonicalPath, safetyResult.Reason);
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = $"Final validation failed: {safetyResult.Reason}";
            return Task.FromResult(result);
        }

        if (safetyResult.IsProtected)
        {
            _logger.LogWarning("Execution blocked: Path {Path} is protected for item {ItemId}.", context.CanonicalPath, context.CleanupPlanItemId);
            result.Outcome = CleanupOutcome.Protected;
            result.FailureReason = "Path is protected.";
            return Task.FromResult(result);
        }

        if (safetyResult.IsReparsePoint)
        {
            _logger.LogWarning("Execution blocked: Path {Path} is a reparse point for item {ItemId}.", context.CanonicalPath, context.CleanupPlanItemId);
            result.Outcome = CleanupOutcome.ReparseBlocked;
            result.FailureReason = "Path is a reparse point.";
            return Task.FromResult(result);
        }

        if (!string.IsNullOrEmpty(context.ExpectedRootPath) && !safetyResult.IsWithinExpectedRoot)
        {
            _logger.LogWarning("Execution blocked: Path {Path} is outside expected root {ExpectedRoot} for item {ItemId}.", context.CanonicalPath, context.ExpectedRootPath, context.CleanupPlanItemId);
            result.Outcome = CleanupOutcome.OutsideExpectedRoot;
            result.FailureReason = "Path is outside expected root.";
            return Task.FromResult(result);
        }

        if (!string.Equals(safetyResult.CanonicalPath, context.CanonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Execution blocked: Canonical path changed from {AuthPath} to {ExecPath} for item {ItemId}.", context.CanonicalPath, safetyResult.CanonicalPath, context.CleanupPlanItemId);
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Canonical path changed between authorization and execution.";
            return Task.FromResult(result);
        }

        bool exists = context.ArtifactType == ArtifactType.Directory 
            ? Directory.Exists(context.CanonicalPath)
            : File.Exists(context.CanonicalPath);

        if (!exists)
        {
            _logger.LogInformation("Target artifact {Path} already absent for item {ItemId}.", context.CanonicalPath, context.CleanupPlanItemId);
            result.Outcome = CleanupOutcome.NotFound;
            result.FailureReason = "Target artifact not found.";
            return Task.FromResult(result);
        }

        // 5 & 6. Execution
        try
        {
            if (context.ArtifactType == ArtifactType.File || context.ArtifactType == ArtifactType.Shortcut)
            {
                _logger.LogInformation("Executing file deletion for item {ItemId} at {Path}", context.CleanupPlanItemId, context.CanonicalPath);
                try { File.SetAttributes(context.CanonicalPath, FileAttributes.Normal); } catch { }
                File.Delete(context.CanonicalPath);

                if (File.Exists(context.CanonicalPath))
                {
                    result.Outcome = CleanupOutcome.VerificationFailed;
                    result.FailureReason = "File.Delete completed but file still exists.";
                    return Task.FromResult(result);
                }

                result.Success = true;
                result.Outcome = CleanupOutcome.DeletedAndVerified;
                _logger.LogInformation("File deletion verified for item {ItemId} at {Path}", context.CleanupPlanItemId, context.CanonicalPath);
                return Task.FromResult(result);
            }
            else if (context.ArtifactType == ArtifactType.Directory)
            {
                ExecuteSafeDirectoryCleanup(context.CanonicalPath, context, result, cancellationToken);
                return Task.FromResult(result);
            }
            else
            {
                result.Outcome = CleanupOutcome.ValidationFailed;
                result.FailureReason = $"Unsupported artifact type for file executor: {context.ArtifactType}";
                return Task.FromResult(result);
            }
        }
        catch (Exception ex)
        {
            MapExceptionToOutcome(ex, result);
            _logger.LogError(ex, "Execution failed for item {ItemId} at {Path}: {Outcome} - {Reason}", context.CleanupPlanItemId, context.CanonicalPath, result.Outcome, result.FailureReason);
            return Task.FromResult(result);
        }
    }

    private void ExecuteSafeDirectoryCleanup(
        string canonicalRoot, 
        AuthorizedExecutionContext context, 
        CleanupExecutionResult result, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting safe recursive directory cleanup. ArtifactId: {ItemId}, OriginalPath: {OriginalPath}, CanonicalRoot: {CanonicalRoot}",
            context.CleanupPlanItemId, context.CanonicalPath, canonicalRoot);

        // 1. Root Reparse Point Check (Defense-in-depth)
        var rootDirInfo = new DirectoryInfo(canonicalRoot);
        if (rootDirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            _logger.LogWarning("Root directory {CanonicalRoot} is a reparse point (junction/symlink). Deleting link only without traversing.", canonicalRoot);
            Directory.Delete(canonicalRoot, false);
            if (Directory.Exists(canonicalRoot))
            {
                result.Outcome = CleanupOutcome.VerificationFailed;
                result.FailureReason = "Failed to unlink root reparse point.";
                return;
            }
            result.Success = true;
            result.Outcome = CleanupOutcome.DeletedAndVerified;
            return;
        }

        // 2. Iterative Child Discovery & Multi-layer Validation (No blind recursive descent)
        var validatedFiles = new List<string>();
        var validatedDirectories = new List<string>();
        var reparseDirectories = new List<string>();

        var dirsToScan = new Queue<string>();
        dirsToScan.Enqueue(canonicalRoot);

        while (dirsToScan.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDir = dirsToScan.Dequeue();

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(currentDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate directory entries in {CurrentDir}", currentDir);
                MapExceptionToOutcome(ex, result);
                return;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string canonicalChild;
                try
                {
                    canonicalChild = Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to canonicalize child path {Entry}", entry);
                    result.Outcome = CleanupOutcome.ValidationFailed;
                    result.FailureReason = $"Invalid child path: {entry}";
                    return;
                }

                // Check 1: Strict Containment (Must not equal root and must reside inside canonicalRoot)
                if (!_pathResolver.IsPathContainedWithin(canonicalChild, canonicalRoot) || 
                    string.Equals(canonicalChild, canonicalRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Child path {CanonicalChild} escapes canonical root {CanonicalRoot}.", canonicalChild, canonicalRoot);
                    result.Outcome = CleanupOutcome.OutsideExpectedRoot;
                    result.FailureReason = $"Child path {canonicalChild} escapes authorized root {canonicalRoot}.";
                    return;
                }

                // Check 2: Child Safety & Protection Check
                var childSafety = _pathResolver.ResolveAndVerify(canonicalChild, canonicalRoot, cancellationToken);
                if (childSafety.IsProtected)
                {
                    _logger.LogError("Directory contains protected child: {CanonicalChild}. Aborting directory cleanup.", canonicalChild);
                    result.Outcome = CleanupOutcome.Protected;
                    result.FailureReason = $"Directory contains protected child: {canonicalChild}";
                    return;
                }

                // Check 3: Reparse Point Check
                FileAttributes childAttrs;
                try
                {
                    childAttrs = File.GetAttributes(canonicalChild);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read attributes for child {CanonicalChild}", canonicalChild);
                    MapExceptionToOutcome(ex, result);
                    return;
                }

                if (childAttrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (childAttrs.HasFlag(FileAttributes.Directory))
                    {
                        _logger.LogInformation("Child junction/symlink directory detected: {CanonicalChild}. Adding to link removal without traversing.", canonicalChild);
                        reparseDirectories.Add(canonicalChild);
                        // CRITICAL: DO NOT enqueue into dirsToScan! We do not follow reparse points.
                    }
                    else
                    {
                        _logger.LogInformation("Child symlink file detected: {CanonicalChild}.", canonicalChild);
                        validatedFiles.Add(canonicalChild);
                    }
                }
                else
                {
                    if (childAttrs.HasFlag(FileAttributes.Directory))
                    {
                        validatedDirectories.Add(canonicalChild);
                        dirsToScan.Enqueue(canonicalChild);
                    }
                    else
                    {
                        validatedFiles.Add(canonicalChild);
                    }
                }
            }
        }

        _logger.LogInformation("Child discovery completed for {CanonicalRoot}. Total items: {Total} ({FileCount} files, {DirCount} directories, {ReparseCount} reparse points).",
            canonicalRoot, validatedFiles.Count + validatedDirectories.Count + reparseDirectories.Count,
            validatedFiles.Count, validatedDirectories.Count, reparseDirectories.Count);

        // 3. Deletion Phase (Fail-Closed, Bottom-Up Execution)
        _logger.LogInformation("Starting bottom-up deletion for {CanonicalRoot}.", canonicalRoot);

        // Step 3a: Delete all validated files
        foreach (var file in validatedFiles)
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch { }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file {File}", file);
                MapExceptionToOutcome(ex, result);
                return;
            }
        }

        // Step 3b: Delete all reparse directories (junctions/symlinks deleted without recursion)
        foreach (var reparseDir in reparseDirectories)
        {
            try
            {
                File.SetAttributes(reparseDir, FileAttributes.Normal);
            }
            catch { }

            try
            {
                Directory.Delete(reparseDir, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete reparse directory link {ReparseDir}", reparseDir);
                MapExceptionToOutcome(ex, result);
                return;
            }
        }

        // Step 3c: Delete all subdirectories from deepest to shallowest
        var sortedDirs = validatedDirectories.OrderByDescending(d => d.Length).ToList();
        foreach (var dir in sortedDirs)
        {
            try
            {
                File.SetAttributes(dir, FileAttributes.Normal);
            }
            catch { }

            try
            {
                Directory.Delete(dir, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete subdirectory {Dir}", dir);
                MapExceptionToOutcome(ex, result);
                return;
            }
        }

        // Step 3d: Delete the canonical root directory itself
        try
        {
            File.SetAttributes(canonicalRoot, FileAttributes.Normal);
        }
        catch { }

        try
        {
            Directory.Delete(canonicalRoot, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete canonical root directory {CanonicalRoot}", canonicalRoot);
            MapExceptionToOutcome(ex, result);
            return;
        }

        // 4. Post-Deletion Verification
        if (Directory.Exists(canonicalRoot))
        {
            _logger.LogError("Verification failed: directory {CanonicalRoot} still exists after cleanup.", canonicalRoot);
            result.Outcome = CleanupOutcome.VerificationFailed;
            result.FailureReason = "Directory still exists after deletion execution.";
            return;
        }

        _logger.LogInformation("Directory cleanup successfully executed and verified for {CanonicalRoot}.", canonicalRoot);
        result.Success = true;
        result.Outcome = CleanupOutcome.DeletedAndVerified;
    }

    private static void MapExceptionToOutcome(Exception ex, CleanupExecutionResult result)
    {
        const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
        const int ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);
        const int ERROR_DIR_NOT_EMPTY = unchecked((int)0x80070091);
        const int ERROR_ACCESS_DENIED = unchecked((int)0x80070005);
        const int ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002);
        const int ERROR_PATH_NOT_FOUND = unchecked((int)0x80070003);

        if (ex.HResult == ERROR_SHARING_VIOLATION || ex.HResult == ERROR_LOCK_VIOLATION)
        {
            result.Outcome = CleanupOutcome.Locked;
        }
        else if (ex.HResult == ERROR_DIR_NOT_EMPTY)
        {
            result.Outcome = CleanupOutcome.DirectoryNotEmpty;
        }
        else if (ex.HResult == ERROR_ACCESS_DENIED || ex is UnauthorizedAccessException)
        {
            result.Outcome = CleanupOutcome.AccessDenied;
        }
        else if (ex.HResult == ERROR_FILE_NOT_FOUND || ex.HResult == ERROR_PATH_NOT_FOUND || ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            result.Outcome = CleanupOutcome.NotFound;
        }
        else
        {
            result.Outcome = CleanupOutcome.DeleteFailed;
        }

        result.FailureReason = ex.Message;
    }
}
