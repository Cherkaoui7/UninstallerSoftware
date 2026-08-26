using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uninstaller.Core.Abstractions;
using Uninstaller.Domain.Entities;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Windows.Filesystem;

public class WindowsShortcutScanner : IResidualScanner
{
    private readonly IFileSystem _fileSystem;
    private readonly IShortcutProvider _shortcutProvider;
    private readonly ILogger<WindowsShortcutScanner> _logger;
    private readonly HashSet<string> _discoveredPaths = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "Windows Shortcut Scanner";

    public WindowsShortcutScanner(IFileSystem fileSystem, IShortcutProvider shortcutProvider, ILogger<WindowsShortcutScanner> logger)
    {
        _fileSystem = fileSystem;
        _shortcutProvider = shortcutProvider;
        _logger = logger;
    }

    public Task<IReadOnlyList<ResidualArtifactCandidate>> ScanAsync(Application application, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("WindowsShortcutScanner is not supported on this platform.");
            return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(Array.Empty<ResidualArtifactCandidate>());
        }

        var candidates = new List<ResidualArtifactCandidate>();
        _discoveredPaths.Clear();

        var appName = NormalizeString(application.Name);
        if (string.IsNullOrWhiteSpace(appName))
        {
            return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(candidates);
        }

        var locations = new[]
        {
            (Environment.SpecialFolder.DesktopDirectory, "User Desktop", false),
            (Environment.SpecialFolder.CommonDesktopDirectory, "Public Desktop", false),
            (Environment.SpecialFolder.StartMenu, "User Start Menu", false),
            (Environment.SpecialFolder.CommonStartMenu, "Common Start Menu", false),
            (Environment.SpecialFolder.Startup, "User Startup", true),
            (Environment.SpecialFolder.CommonStartup, "Common Startup", true)
        };

        foreach (var (folder, sourceName, isStartup) in locations)
        {
            try
            {
                var basePath = Environment.GetFolderPath(folder);
                if (string.IsNullOrWhiteSpace(basePath) || !_fileSystem.Directory.Exists(basePath))
                    continue;

                ScanDirectory(basePath, sourceName, isStartup, appName, application, candidates, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to scan shortcut location {SourceName}", sourceName);
            }
        }

        return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(candidates);
    }

    private void ScanDirectory(
        string path, 
        string sourceName, 
        bool isStartup, 
        string normalizedAppName, 
        Application app, 
        List<ResidualArtifactCandidate> candidates, 
        CancellationToken cancellationToken)
    {
        try
        {
            var files = _fileSystem.Directory.GetFiles(path, "*.lnk", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_discoveredPaths.Add(file)) continue; // Deduplication

                ProcessShortcut(file, sourceName, isStartup, normalizedAppName, app, candidates);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Access denied scanning directory {Path}", path);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogDebug(ex, "Directory not found {Path}", path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unexpected error scanning directory {Path}", path);
        }
    }

    private void ProcessShortcut(
        string shortcutPath, 
        string sourceName, 
        bool isStartup, 
        string normalizedAppName, 
        Application app, 
        List<ResidualArtifactCandidate> candidates)
    {
        var shortcutInfo = _shortcutProvider.GetShortcutInfo(shortcutPath);
        if (shortcutInfo == null) return; // Malformed or unreadable shortcut

        var evidence = new List<Evidence>();
        var shortcutFileName = _fileSystem.Path.GetFileNameWithoutExtension(shortcutPath);
        var normalizedShortcutName = NormalizeString(shortcutFileName);

        // Primary Name match evidence
        if (normalizedShortcutName.Equals(normalizedAppName, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new Evidence(EvidenceType.ShortcutNameMatch, $"Shortcut name matches application '{app.Name}'", Name));
        }

        // Primary Target match evidence
        if (!string.IsNullOrWhiteSpace(shortcutInfo.TargetPath))
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                // Exact install location match
                if (shortcutInfo.TargetPath.Equals(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(new Evidence(EvidenceType.ExactShortcutTarget, "Target exactly matches InstallLocation", Name));
                }
                // Under install location
                else if (shortcutInfo.TargetPath.StartsWith(app.InstallLocation.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(new Evidence(EvidenceType.InstallLocationTargetMatch, "Target resides within InstallLocation", Name));
                }
            }
        }

        // Only generate secondary evidence (Broken/Startup) if we have primary evidence linking the shortcut to the application
        if (evidence.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(shortcutInfo.TargetPath))
            {
                var targetExists = _fileSystem.File.Exists(shortcutInfo.TargetPath) || _fileSystem.Directory.Exists(shortcutInfo.TargetPath);
                if (!targetExists)
                {
                    evidence.Add(new Evidence(EvidenceType.BrokenShortcutTarget, $"Shortcut target does not exist: {shortcutInfo.TargetPath}", Name));
                }
            }

            if (isStartup)
            {
                evidence.Add(new Evidence(EvidenceType.StartupLocation, $"Shortcut located in {sourceName}", Name));
            }

            candidates.Add(new ResidualArtifactCandidate(
                new Artifact
                {
                    Id = Guid.NewGuid(),
                    Path = shortcutPath,
                    Name = shortcutFileName,
                    Type = ArtifactType.Shortcut,
                    DiscoveredAt = DateTime.UtcNow
                },
                evidence,
                Name
            ));
        }
    }

    private static string NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
    }
}
