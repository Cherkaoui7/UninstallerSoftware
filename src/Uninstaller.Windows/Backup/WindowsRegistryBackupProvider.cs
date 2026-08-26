using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Backup;

public class RegistryBackupManifest
{
    public string KeyPath { get; set; } = string.Empty;
    public List<RegistryValueBackup> Values { get; set; } = new();
    public List<RegistryBackupManifest> SubKeys { get; set; } = new();
}

public class RegistryValueBackup
{
    public string Name { get; set; } = string.Empty;
    public RegistryValueKind Kind { get; set; }
    public byte[] RawData { get; set; } = Array.Empty<byte>();
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsRegistryBackupProvider : IRegistryBackupProvider
{
    private readonly IBackupStorage _storage;

    public WindowsRegistryBackupProvider(IBackupStorage storage)
    {
        _storage = storage;
    }

    public Task<Uninstaller.Domain.Entities.Backup> BackupRegistryArtifactAsync(CleanupPlanItem item, string sessionBackupDirectory, CancellationToken cancellationToken = default)
    {
        var backup = new Uninstaller.Domain.Entities.Backup
        {
            ArtifactId = Guid.NewGuid(),
            ArtifactType = item.ArtifactType,
            OriginalPath = item.Path,
            Status = BackupStatus.Pending
        };

        try
        {
            backup.Status = BackupStatus.Writing;

            var destName = backup.ArtifactId.ToString("N") + ".reg.json";
            var destPath = Path.Combine(sessionBackupDirectory, destName);

            if (!_storage.IsPathWithinControlledRoot(destPath))
            {
                throw new InvalidOperationException("Destination path escapes controlled backup root.");
            }

            var parts = item.Path.Split('\\', 2);
            if (parts.Length < 2) throw new InvalidOperationException("Invalid registry path format.");

            var hiveString = parts[0];
            var subKeyString = parts[1];

            using var baseKey = GetBaseKey(hiveString);
            if (baseKey == null) throw new InvalidOperationException($"Unsupported registry hive: {hiveString}");

            if (item.ArtifactType == ArtifactType.RegistryValue)
            {
                // Format: HKLM\Software\Key::ValueName
                var valueParts = subKeyString.Split("::");
                if (valueParts.Length != 2) throw new InvalidOperationException("Invalid registry value path format.");

                var keyPath = valueParts[0];
                var valueName = valueParts[1];

                using var subKey = baseKey.OpenSubKey(keyPath, writable: false);
                if (subKey == null) throw new InvalidOperationException($"Source registry key not found: {keyPath}");

                var valueKind = subKey.GetValueKind(valueName);
                if (valueKind == RegistryValueKind.None) throw new InvalidOperationException($"Source registry value not found: {valueName}");

                var manifest = new RegistryBackupManifest { KeyPath = item.Path };
                
                var data = GetRegistryValueAsBytes(subKey, valueName, valueKind);
                manifest.Values.Add(new RegistryValueBackup { Name = valueName, Kind = valueKind, RawData = data });

                File.WriteAllText(destPath, JsonSerializer.Serialize(manifest));
            }
            else if (item.ArtifactType == ArtifactType.RegistryKey)
            {
                using var subKey = baseKey.OpenSubKey(subKeyString, writable: false);
                if (subKey == null) throw new InvalidOperationException($"Source registry key not found: {subKeyString}");

                var manifest = ExportKeyRecursively(subKey, item.Path);
                
                File.WriteAllText(destPath, JsonSerializer.Serialize(manifest));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported artifact type for registry backup: {item.ArtifactType}");
            }

            var fileInfo = new FileInfo(destPath);
            backup.Size = fileInfo.Length;
            backup.Hash = ComputeSha256(destPath);
            backup.BackupPath = destPath;
            backup.Status = BackupStatus.Verifying;

            return Task.FromResult(backup);
        }
        catch (Exception ex)
        {
            backup.Status = BackupStatus.Failed;
            backup.FailureReason = ex.Message;
            return Task.FromResult(backup);
        }
    }

    private RegistryBackupManifest ExportKeyRecursively(RegistryKey key, string fullPath)
    {
        var manifest = new RegistryBackupManifest { KeyPath = fullPath };

        foreach (var valueName in key.GetValueNames())
        {
            var valueKind = key.GetValueKind(valueName);
            var data = GetRegistryValueAsBytes(key, valueName, valueKind);
            manifest.Values.Add(new RegistryValueBackup { Name = valueName, Kind = valueKind, RawData = data });
        }

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var childKey = key.OpenSubKey(subKeyName, writable: false);
            if (childKey != null)
            {
                manifest.SubKeys.Add(ExportKeyRecursively(childKey, fullPath + "\\" + subKeyName));
            }
        }

        return manifest;
    }

    private byte[] GetRegistryValueAsBytes(RegistryKey key, string valueName, RegistryValueKind valueKind)
    {
        var val = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (val == null) return Array.Empty<byte>();

        return valueKind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => Encoding.Unicode.GetBytes((string)val),
            RegistryValueKind.MultiString => Encoding.Unicode.GetBytes(string.Join("\0", (string[])val) + "\0\0"),
            RegistryValueKind.DWord => BitConverter.GetBytes((int)val),
            RegistryValueKind.QWord => BitConverter.GetBytes((long)val),
            RegistryValueKind.Binary => (byte[])val,
            _ => Array.Empty<byte>()
        };
    }

    public Task<BackupVerificationResult> VerifyRegistryBackupAsync(Uninstaller.Domain.Entities.Backup backup, CancellationToken cancellationToken = default)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));

        if (backup.Status == BackupStatus.Failed)
        {
            return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup already failed." });
        }

        try
        {
            if (!_storage.IsPathWithinControlledRoot(backup.BackupPath))
            {
                return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup path escapes controlled root." });
            }

            if (!File.Exists(backup.BackupPath))
            {
                return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup file is missing." });
            }

            var currentHash = ComputeSha256(backup.BackupPath);
            var currentSize = new FileInfo(backup.BackupPath).Length;

            if (currentHash != backup.Hash || currentSize != backup.Size)
            {
                return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Integrity check failed (hash or size mismatch)." });
            }

            var manifestContent = File.ReadAllText(backup.BackupPath);
            var manifest = JsonSerializer.Deserialize<RegistryBackupManifest>(manifestContent);

            if (manifest == null)
            {
                return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = "Backup manifest is malformed." });
            }

            return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult
            {
                IsValid = true,
                Hash = backup.Hash,
                Size = backup.Size,
                VerifiedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new Uninstaller.Domain.Entities.BackupVerificationResult { IsValid = false, FailureReason = ex.Message });
        }
    }

    public Task RestoreRegistryBackupAsync(Uninstaller.Domain.Entities.Backup backup, string testFixtureRoot, CancellationToken cancellationToken = default)
    {
        // For testing purposes, we deserialize the backup and we could theoretically re-apply it to an isolated hive.
        // In our tests, we will just parse the manifest to ensure we can read all native values.
        var manifestContent = File.ReadAllText(backup.BackupPath);
        var manifest = JsonSerializer.Deserialize<RegistryBackupManifest>(manifestContent);
        
        if (manifest == null) throw new InvalidOperationException("Corrupt registry backup manifest.");
        
        // testFixtureRoot is assumed to be the target base path (e.g. HKCU\Software\RestoredApp)
        var parts = testFixtureRoot.Split('\\', 2);
        if (parts.Length < 2) throw new InvalidOperationException("Invalid registry test fixture root format.");

        var hiveString = parts[0];
        var subKeyString = parts[1];

        using var baseKey = GetBaseKey(hiveString);
        if (baseKey == null) throw new InvalidOperationException($"Unsupported registry hive: {hiveString}");

        using var targetKey = baseKey.CreateSubKey(subKeyString);
        if (targetKey == null) throw new InvalidOperationException($"Could not create target registry key: {subKeyString}");

        if (backup.ArtifactType == ArtifactType.RegistryValue)
        {
            if (manifest.Values.Count == 1)
            {
                RestoreValue(targetKey, manifest.Values[0]);
            }
        }
        else if (backup.ArtifactType == ArtifactType.RegistryKey)
        {
            RestoreKeyRecursively(targetKey, manifest);
        }

        return Task.CompletedTask;
    }

    private void RestoreKeyRecursively(RegistryKey targetKey, RegistryBackupManifest manifest)
    {
        foreach (var val in manifest.Values)
        {
            RestoreValue(targetKey, val);
        }

        foreach (var subManifest in manifest.SubKeys)
        {
            var subKeyName = subManifest.KeyPath.Substring(subManifest.KeyPath.LastIndexOf('\\') + 1);
            using var childKey = targetKey.CreateSubKey(subKeyName);
            if (childKey != null)
            {
                RestoreKeyRecursively(childKey, subManifest);
            }
        }
    }

    private void RestoreValue(RegistryKey targetKey, RegistryValueBackup val)
    {
        object? decodedValue = val.Kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => Encoding.Unicode.GetString(val.RawData),
            RegistryValueKind.MultiString => Encoding.Unicode.GetString(val.RawData).TrimEnd('\0').Split('\0'),
            RegistryValueKind.DWord => BitConverter.ToInt32(val.RawData),
            RegistryValueKind.QWord => BitConverter.ToInt64(val.RawData),
            RegistryValueKind.Binary => val.RawData,
            _ => null
        };

        if (decodedValue != null)
        {
            targetKey.SetValue(val.Name, decodedValue, val.Kind);
        }
    }

    private RegistryKey? GetBaseKey(string root)
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

    private string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
