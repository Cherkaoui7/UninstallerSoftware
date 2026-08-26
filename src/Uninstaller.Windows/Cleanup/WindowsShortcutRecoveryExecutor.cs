using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;
using Uninstaller.Windows.Filesystem;

namespace Uninstaller.Windows.Cleanup;

public class WindowsShortcutRecoveryExecutor : IShortcutRecoveryExecutor
{
    private readonly ICanonicalPathResolver _pathResolver;
    private readonly IBackupStorage _backupStorage;
    private readonly IShortcutProvider _shortcutProvider;
    private static readonly string[] StartupRoots = {
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
    };

    public WindowsShortcutRecoveryExecutor(
        ICanonicalPathResolver pathResolver, 
        IBackupStorage backupStorage,
        IShortcutProvider shortcutProvider)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _backupStorage = backupStorage ?? throw new ArgumentNullException(nameof(backupStorage));
        _shortcutProvider = shortcutProvider ?? throw new ArgumentNullException(nameof(shortcutProvider));
    }

    public async Task<RecoveryResult> ExecuteAsync(RecoveryContext context, CancellationToken cancellationToken = default)
    {
        var result = new RecoveryResult { RecoveryItemId = context.RecoveryItemId };

        if (!context.BackupVerificationResult.IsValid)
        {
            result.Outcome = RecoveryOutcome.BackupInvalid;
            result.FailureReason = "Backup verification failed.";
            return result;
        }

        if (context.ArtifactType != ArtifactType.Shortcut)
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = $"Unsupported artifact type for shortcut executor: {context.ArtifactType}";
            return result;
        }

        var targetSafety = _pathResolver.ResolveAndVerify(context.OriginalCanonicalPath, null, cancellationToken);
        if (!targetSafety.IsValid)
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = $"Target path is invalid: {targetSafety.Reason}";
            return result;
        }

        if (targetSafety.IsProtected)
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = "Target path is protected.";
            return result;
        }

        if (!targetSafety.CanonicalPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = "Target does not have a .lnk extension.";
            return result;
        }

        if (File.Exists(targetSafety.CanonicalPath))
        {
            result.Outcome = RecoveryOutcome.RecoveryConflict;
            result.FailureReason = "Shortcut already exists at the target path.";
            return result;
        }

        if (!File.Exists(context.BackupPath))
        {
            result.Outcome = RecoveryOutcome.BackupInvalid;
            result.FailureReason = "Backup file not found.";
            return result;
        }

        string stagingDir = Path.Combine(_backupStorage.GetBackupRoot(), "Staging");
        Directory.CreateDirectory(stagingDir);
        string stagingFile = Path.Combine(stagingDir, Guid.NewGuid().ToString() + ".lnk"); // Must have .lnk for WScript to parse it

        try
        {
            // 1. Stage the shortcut file
            File.Copy(context.BackupPath, stagingFile, overwrite: false);

            // 2. Hash verification of staging file
            string stagedHash = await CalculateHashAsync(stagingFile, cancellationToken);
            if (!string.Equals(stagedHash, context.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                result.Outcome = RecoveryOutcome.VerificationFailed;
                result.FailureReason = "Staged file hash mismatch.";
                return result;
            }

            // 3. Metadata validation of staged shortcut
            var liveInfo = _shortcutProvider.GetShortcutInfo(stagingFile);
            if (liveInfo == null)
            {
                result.Outcome = RecoveryOutcome.ValidationFailed;
                result.FailureReason = "Failed to read shortcut metadata from staged file.";
                return result;
            }

            if (!string.IsNullOrEmpty(context.ExpectedShortcutTarget))
            {
                var liveTarget = liveInfo.TargetPath ?? string.Empty;
                if (!string.Equals(liveTarget, context.ExpectedShortcutTarget, StringComparison.OrdinalIgnoreCase))
                {
                    result.Outcome = RecoveryOutcome.ValidationFailed;
                    result.FailureReason = $"Shortcut target mismatch. Expected: '{context.ExpectedShortcutTarget}', Actual: '{liveTarget}'.";
                    return result;
                }
            }

            // 4. Final Destination validation
            if (File.Exists(targetSafety.CanonicalPath))
            {
                result.Outcome = RecoveryOutcome.RecoveryConflict;
                result.FailureReason = "Target shortcut appeared during staging.";
                return result;
            }

            var dir = Path.GetDirectoryName(targetSafety.CanonicalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 5. Controlled Placement
            File.Move(stagingFile, targetSafety.CanonicalPath);

            // 6. Final verification
            if (!File.Exists(targetSafety.CanonicalPath))
            {
                result.Outcome = RecoveryOutcome.VerificationFailed;
                result.FailureReason = "Shortcut does not exist after placement.";
                return result;
            }

            string finalHash = await CalculateHashAsync(targetSafety.CanonicalPath, cancellationToken);
            if (!string.Equals(finalHash, context.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                result.Outcome = RecoveryOutcome.VerificationFailed;
                result.FailureReason = "Final file hash mismatch.";
                return result;
            }

            // Optional: Re-verify target metadata at final destination
            var finalInfo = _shortcutProvider.GetShortcutInfo(targetSafety.CanonicalPath);
            if (finalInfo == null || (!string.IsNullOrEmpty(context.ExpectedShortcutTarget) && !string.Equals(finalInfo.TargetPath, context.ExpectedShortcutTarget, StringComparison.OrdinalIgnoreCase)))
            {
                result.Outcome = RecoveryOutcome.VerificationFailed;
                result.FailureReason = "Shortcut metadata drifted after placement.";
                return result;
            }

            result.Outcome = RecoveryOutcome.Recovered;
            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Outcome = RecoveryOutcome.AccessDenied;
            result.FailureReason = ex.Message;
            return result;
        }
        catch (IOException ex)
        {
            result.Outcome = RecoveryOutcome.Locked;
            result.FailureReason = ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Outcome = RecoveryOutcome.Failed;
            result.FailureReason = ex.Message;
            return result;
        }
        finally
        {
            if (File.Exists(stagingFile))
            {
                try { File.Delete(stagingFile); } catch { }
            }
        }
    }

    private static async Task<string> CalculateHashAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
