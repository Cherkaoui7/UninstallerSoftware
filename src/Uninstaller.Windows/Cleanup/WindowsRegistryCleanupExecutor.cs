using System;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Cleanup;

public class WindowsRegistryCleanupExecutor : IRegistryCleanupExecutor
{
    private static readonly string[] ProtectedRoots = new[]
    {
        "Software",
        "Software\\Microsoft",
        "Software\\Microsoft\\Windows",
        "Software\\Microsoft\\Windows\\CurrentVersion",
        "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
        "Software\\Classes",
        "System",
        "System\\CurrentControlSet"
    };

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

        // Final Validation (TOCTOU)
        var parts = context.CanonicalPath.Split('\\', 2);
        if (parts.Length < 2)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = "Malformed registry path.";
            result.WasFinalValidationPerformed = true;
            return Task.FromResult(result);
        }

        var hiveString = parts[0];
        var subKeyString = parts[1];

        // Ensure we are not deleting a protected root
        var subKeyLower = subKeyString.ToLowerInvariant();
        if (ProtectedRoots.Any(pr => pr.Equals(subKeyLower, StringComparison.OrdinalIgnoreCase)))
        {
            result.Outcome = CleanupOutcome.Protected;
            result.FailureReason = "Target is a protected registry root.";
            result.WasFinalValidationPerformed = true;
            return Task.FromResult(result);
        }

        result.WasFinalValidationPerformed = true;

        using var baseKey = GetBaseKey(hiveString);
        if (baseKey == null)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = $"Unsupported hive: {hiveString}";
            return Task.FromResult(result);
        }

        try
        {
            if (context.ArtifactType == ArtifactType.RegistryKey)
            {
                using var checkKey = baseKey.OpenSubKey(subKeyString);
                if (checkKey == null)
                {
                    result.Outcome = CleanupOutcome.NotFound;
                    result.FailureReason = "Registry key not found.";
                    return Task.FromResult(result);
                }

                baseKey.DeleteSubKeyTree(subKeyString, throwOnMissingSubKey: false);
            }
            else if (context.ArtifactType == ArtifactType.RegistryValue)
            {
                var valueParts = subKeyString.Split("::");
                if (valueParts.Length != 2)
                {
                    result.Outcome = CleanupOutcome.ValidationFailed;
                    result.FailureReason = "Malformed registry value path.";
                    return Task.FromResult(result);
                }

                var keyPath = valueParts[0];
                var valueName = valueParts[1];

                using var key = baseKey.OpenSubKey(keyPath, writable: true);
                if (key == null)
                {
                    result.Outcome = CleanupOutcome.NotFound;
                    result.FailureReason = "Registry key for value not found.";
                    return Task.FromResult(result);
                }

                if (key.GetValue(valueName) == null)
                {
                    result.Outcome = CleanupOutcome.NotFound;
                    result.FailureReason = "Registry value not found.";
                    return Task.FromResult(result);
                }

                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                result.Outcome = CleanupOutcome.ValidationFailed;
                result.FailureReason = $"Unsupported artifact type for registry executor: {context.ArtifactType}";
                return Task.FromResult(result);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Outcome = CleanupOutcome.AccessDenied;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (SecurityException ex)
        {
            result.Outcome = CleanupOutcome.AccessDenied;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (ArgumentException ex)
        {
            result.Outcome = CleanupOutcome.ValidationFailed;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            result.Outcome = CleanupOutcome.DeleteFailed;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }

        // Verification
        bool stillExists = false;
        if (context.ArtifactType == ArtifactType.RegistryKey)
        {
            using var checkKey = baseKey.OpenSubKey(subKeyString);
            stillExists = checkKey != null;
        }
        else if (context.ArtifactType == ArtifactType.RegistryValue)
        {
            var valueParts = subKeyString.Split("::");
            using var key = baseKey.OpenSubKey(valueParts[0]);
            stillExists = key != null && key.GetValue(valueParts[1]) != null;
        }

        if (stillExists)
        {
            result.Outcome = CleanupOutcome.VerificationFailed;
            result.FailureReason = "Registry artifact still exists after deletion attempt.";
            result.Success = false;
        }
        else
        {
            result.Outcome = CleanupOutcome.DeletedAndVerified;
            result.Success = true;
        }

        return Task.FromResult(result);
    }

    private RegistryKey GetBaseKey(string root)
    {
        return root.ToUpperInvariant() switch
        {
            "HKEY_CLASSES_ROOT" or "HKCR" => Microsoft.Win32.Registry.ClassesRoot,
            "HKEY_CURRENT_USER" or "HKCU" => Microsoft.Win32.Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Microsoft.Win32.Registry.LocalMachine,
            "HKEY_USERS" or "HKU" => Microsoft.Win32.Registry.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => Microsoft.Win32.Registry.CurrentConfig,
            _ => null
        };
    }
}
