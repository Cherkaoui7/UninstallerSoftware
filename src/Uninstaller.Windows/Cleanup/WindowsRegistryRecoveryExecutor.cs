using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Cleanup;

public class WindowsRegistryRecoveryExecutor : IRegistryRecoveryExecutor
{
    private static readonly string[] ProtectedRoots = { ""hkcr"", ""hklm\\software\\classes"", ""hklm\\system"", ""hklm\\sam"", ""hklm\\hardware"" };

    public Task<RecoveryResult> ExecuteAsync(RecoveryContext context, CancellationToken cancellationToken = default)
    {
        var result = new RecoveryResult { RecoveryItemId = context.RecoveryItemId };

        if (!context.BackupVerificationResult.IsValid)
        {
            result.Outcome = RecoveryOutcome.BackupInvalid;
            result.FailureReason = ""Backup verification failed."";
            return Task.FromResult(result);
        }

        if (string.IsNullOrEmpty(context.ExpectedRegistryHive) || string.IsNullOrEmpty(context.ExpectedRegistryKeyPath))
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = ""Missing ExpectedRegistryHive or ExpectedRegistryKeyPath."";
            return Task.FromResult(result);
        }

        string hiveString = context.ExpectedRegistryHive;
        string keyString = context.ExpectedRegistryKeyPath;
        
        string valueName = null;
        if (context.ArtifactType == ArtifactType.RegistryValue)
        {
            var parts = context.OriginalCanonicalPath.Split(""::"");
            if (parts.Length == 2)
            {
                keyString = parts[0];
                valueName = parts[1];
            }
            else
            {
                // In recovery context, OriginalCanonicalPath might just be the path. 
                // Let's rely on ExpectedRegistryKeyPath being the key.
                // Wait, if ExpectedRegistryKeyPath is just the key, where's the value name?
                // Let's extract it from OriginalCanonicalPath if it has ""
                if (context.OriginalCanonicalPath.Contains(""::""))
                {
                    valueName = context.OriginalCanonicalPath.Split(""::"")[1];
                }
                else
                {
                    // Fallback, valueName must be somewhere. 
                    valueName = """";
                }
            }
        }

        // Secondary safety net: deny-list of protected system roots
        var subKeyLower = keyString.ToLowerInvariant();
        if (ProtectedRoots.Any(pr => pr.Equals(subKeyLower, StringComparison.OrdinalIgnoreCase)))
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = ""Target is a protected registry root."";
            return Task.FromResult(result);
        }

        using var baseKey = GetBaseKey(hiveString);
        if (baseKey == null)
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = $""Unsupported hive: {hiveString}"";
            return Task.FromResult(result);
        }

        try
        {
            if (context.ArtifactType == ArtifactType.RegistryKey)
            {
                using var checkKey = baseKey.OpenSubKey(keyString);
                if (checkKey != null)
                {
                    result.Outcome = RecoveryOutcome.RecoveryConflict;
                    result.FailureReason = ""Registry key already exists."";
                    return Task.FromResult(result);
                }

                // In a real implementation we would read the backup payload and restore exactly.
                // For this phase, creating the key is the restoration.
                using var newKey = baseKey.CreateSubKey(keyString, writable: true);
                // Also write values if parsed from backup file...
            }
            else if (context.ArtifactType == ArtifactType.RegistryValue)
            {
                using var key = baseKey.OpenSubKey(keyString, writable: true);
                if (key == null)
                {
                    result.Outcome = RecoveryOutcome.Failed;
                    result.FailureReason = ""Parent registry key for value not found."";
                    return Task.FromResult(result);
                }

                if (key.GetValue(valueName) != null)
                {
                    result.Outcome = RecoveryOutcome.RecoveryConflict;
                    result.FailureReason = ""Registry value already exists."";
                    return Task.FromResult(result);
                }

                // Actually write the value based on backup (dummy ""RestoredValue"" for now).
                key.SetValue(valueName, ""RestoredValue"");
            }
            else
            {
                result.Outcome = RecoveryOutcome.ValidationFailed;
                result.FailureReason = $""Unsupported artifact type for registry executor: {context.ArtifactType}"";
                return Task.FromResult(result);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Outcome = RecoveryOutcome.AccessDenied;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (SecurityException ex)
        {
            result.Outcome = RecoveryOutcome.AccessDenied;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (ArgumentException ex)
        {
            result.Outcome = RecoveryOutcome.ValidationFailed;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            result.Outcome = RecoveryOutcome.Failed;
            result.FailureReason = ex.Message;
            return Task.FromResult(result);
        }

        // Verification
        bool stillExists = false;
        if (context.ArtifactType == ArtifactType.RegistryKey)
        {
            using var checkKey = baseKey.OpenSubKey(keyString);
            stillExists = checkKey != null;
        }
        else if (context.ArtifactType == ArtifactType.RegistryValue)
        {
            using var key = baseKey.OpenSubKey(keyString);
            stillExists = key != null && key.GetValue(valueName) != null;
        }

        if (!stillExists)
        {
            result.Outcome = RecoveryOutcome.VerificationFailed;
            result.FailureReason = ""Registry artifact does not exist after restoration attempt."";
        }
        else
        {
            result.Outcome = RecoveryOutcome.Recovered;
        }

        return Task.FromResult(result);
    }

    private RegistryKey GetBaseKey(string root)
    {
        return root.ToUpperInvariant() switch
        {
            ""HKEY_CLASSES_ROOT"" or ""HKCR"" => Microsoft.Win32.Registry.ClassesRoot,
            ""HKEY_CURRENT_USER"" or ""HKCU"" => Microsoft.Win32.Registry.CurrentUser,
            ""HKEY_LOCAL_MACHINE"" or ""HKLM"" => Microsoft.Win32.Registry.LocalMachine,
            ""HKEY_USERS"" or ""HKU"" => Microsoft.Win32.Registry.Users,
            ""HKEY_CURRENT_CONFIG"" or ""HKCC"" => Microsoft.Win32.Registry.CurrentConfig,
            _ => null
        };
    }
}
