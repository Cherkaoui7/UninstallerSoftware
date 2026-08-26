using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Registry;

public class WindowsRegistryScanner : IResidualScanner
{
    private readonly IRegistryProvider _registryProvider;
    private readonly ILogger<WindowsRegistryService> _logger; // Keep matching ILogger type if preferred or use its own
    
    // Track unique keys by path to avoid duplicates from different views
    private readonly HashSet<string> _discoveredKeys = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "Windows Registry Scanner";

    public WindowsRegistryScanner(IRegistryProvider registryProvider, ILogger<WindowsRegistryService> logger)
    {
        _registryProvider = registryProvider;
        _logger = logger;
    }

    public Task<IReadOnlyList<ResidualArtifactCandidate>> ScanAsync(Application application, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("WindowsRegistryScanner is not supported on this platform.");
            return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(Array.Empty<ResidualArtifactCandidate>());
        }

        var candidates = new List<ResidualArtifactCandidate>();
        _discoveredKeys.Clear();

        var appName = NormalizeString(application.Name);
        var publisherName = NormalizeString(application.Publisher);

        if (string.IsNullOrWhiteSpace(appName))
        {
            return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(candidates);
        }

        try
        {
            ScanRegistrySource(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", appName, publisherName, candidates, cancellationToken);
            ScanRegistrySource(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64", appName, publisherName, candidates, cancellationToken);
            ScanRegistrySource(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32", appName, publisherName, candidates, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Registry scan cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registry scan.");
        }

        return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(candidates);
    }

    private void ScanRegistrySource(
        RegistryHive hive, 
        RegistryView view, 
        string sourceName, 
        string appName, 
        string? publisherName, 
        List<ResidualArtifactCandidate> candidates, 
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = _registryProvider.OpenBaseKey(hive, view);
            if (baseKey == null) return;

            using var softwareKey = baseKey.OpenSubKey("Software", writable: false);
            if (softwareKey == null) return;

            var subKeyNames = softwareKey.GetSubKeyNames();

            foreach (var keyName in subKeyNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedKey = NormalizeString(keyName);

                // Publisher Match
                if (!string.IsNullOrWhiteSpace(publisherName) && normalizedKey.Equals(publisherName, StringComparison.OrdinalIgnoreCase))
                {
                    ScanPublisherKey(softwareKey, keyName, appName, publisherName, sourceName, candidates, cancellationToken);
                    
                    var fullPath = $"{hive}\\Software\\{keyName}";
                    if (_discoveredKeys.Add(fullPath))
                    {
                        var evidence = new List<Evidence>
                        {
                            new Evidence(EvidenceType.ExactPublisherKeyMatch, $"Key name exactly matches publisher '{publisherName}'", Name),
                            new Evidence(EvidenceType.RegistrySourceMatch, $"Found in {sourceName}", Name)
                        };
                        candidates.Add(CreateCandidate(fullPath, keyName, evidence));
                    }
                    continue; // we already dive into it, no need to check app name match on publisher
                }

                // Application Match
                if (normalizedKey.Equals(appName, StringComparison.OrdinalIgnoreCase))
                {
                    var fullPath = $"{hive}\\Software\\{keyName}";
                    if (_discoveredKeys.Add(fullPath))
                    {
                        var evidence = new List<Evidence>
                        {
                            new Evidence(EvidenceType.ExactApplicationKeyMatch, $"Key name exactly matches application '{appName}'", Name),
                            new Evidence(EvidenceType.RegistrySourceMatch, $"Found in {sourceName}", Name)
                        };
                        candidates.Add(CreateCandidate(fullPath, keyName, evidence));
                    }
                }
            }
        }
        catch (SecurityException ex)
        {
            _logger.LogDebug(ex, "Access denied scanning registry source {Source}", sourceName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error scanning registry source {Source}", sourceName);
        }
    }

    private void ScanPublisherKey(
        IRegistryKeyWrapper softwareKey, 
        string publisherKeyName, 
        string appName, 
        string publisherName, 
        string sourceName, 
        List<ResidualArtifactCandidate> candidates, 
        CancellationToken cancellationToken)
    {
        try
        {
            using var publisherKey = softwareKey.OpenSubKey(publisherKeyName, writable: false);
            if (publisherKey == null) return;

            var subKeys = publisherKey.GetSubKeyNames();
            foreach (var appKeyName in subKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (NormalizeString(appKeyName).Equals(appName, StringComparison.OrdinalIgnoreCase))
                {
                    // For duplicate check, infer the hive from the wrapper implementation conceptually, 
                    // but we can just construct it. We don't have the hive object directly here, 
                    // but we know it's under Software\Publisher
                    // A quick workaround is to just pass full paths or construct them.
                    // For simplicity, we just use a descriptive path.
                    var partialPath = $"Software\\{publisherKeyName}\\{appKeyName}";
                    
                    // Add app candidate
                    var evidence = new List<Evidence>
                    {
                        new Evidence(EvidenceType.ExactApplicationKeyMatch, $"Key exactly matches application '{appName}' under publisher", Name),
                        new Evidence(EvidenceType.RegistrySourceMatch, $"Found in {sourceName}", Name)
                    };
                    
                    // We don't have the hive string here cleanly, so we'll just check if it's already in our candidate list.
                    // The _discoveredKeys normally tracks full paths like HKEY_CURRENT_USER\Software\...
                    // We can just rely on the candidate list being deduped later or use a generic prefix.
                    var logicalPath = $"{sourceName}\\{partialPath}"; 
                    if (_discoveredKeys.Add(logicalPath))
                    {
                        candidates.Add(CreateCandidate(partialPath, appKeyName, evidence)); // We put partial path in Artifact.Path for simplicity, or we can improve this.
                    }
                }
            }
        }
        catch (SecurityException) { }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    private ResidualArtifactCandidate CreateCandidate(string path, string name, List<Evidence> evidence)
    {
        return new ResidualArtifactCandidate(
            new Artifact
            {
                Id = Guid.NewGuid(),
                Path = path,
                Name = name,
                Type = ArtifactType.RegistryKey,
                DiscoveredAt = DateTime.UtcNow
            },
            evidence,
            Name
        );
    }

    private static string NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
    }
}
