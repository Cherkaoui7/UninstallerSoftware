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

public class WindowsFilesystemScanner : IResidualScanner
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<WindowsFilesystemScanner> _logger;
    private readonly HashSet<string> _scannedPaths = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "Windows Filesystem Scanner";

    public WindowsFilesystemScanner(IFileSystem fileSystem, ILogger<WindowsFilesystemScanner> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public Task<IReadOnlyList<ResidualArtifactCandidate>> ScanAsync(Application application, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("WindowsFilesystemScanner is not supported on this platform.");
            return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(Array.Empty<ResidualArtifactCandidate>());
        }

        var candidates = new List<ResidualArtifactCandidate>();
        _scannedPaths.Clear();

        try
        {
            // 1. Install Location
            ScanInstallLocation(application, candidates);
            cancellationToken.ThrowIfCancellationRequested();

            var appName = NormalizeString(application.Name);
            var publisherName = NormalizeString(application.Publisher);

            if (string.IsNullOrWhiteSpace(appName))
            {
                _logger.LogWarning("Application name is empty. Skipping broad filesystem scan.");
                return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(candidates);
            }

            // Target locations
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // 2. ProgramData
            ScanTargetedDirectory(programData, appName, publisherName, candidates, cancellationToken);

            // 3. LocalAppData
            ScanTargetedDirectory(localAppData, appName, publisherName, candidates, cancellationToken);

            // 4. Roaming AppData
            ScanTargetedDirectory(roamingAppData, appName, publisherName, candidates, cancellationToken);

            // 5. Program Files / Program Files (x86)
            ScanTargetedDirectory(programFiles, appName, publisherName, candidates, cancellationToken);
            if (!programFiles.Equals(programFilesX86, StringComparison.OrdinalIgnoreCase))
            {
                ScanTargetedDirectory(programFilesX86, appName, publisherName, candidates, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Filesystem scan cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during filesystem scan.");
        }

        return Task.FromResult<IReadOnlyList<ResidualArtifactCandidate>>(candidates);
    }

    private void ScanInstallLocation(Application application, List<ResidualArtifactCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(application.InstallLocation)) return;

        var path = _fileSystem.Path.GetFullPath(application.InstallLocation.Trim('"'));

        if (!_fileSystem.Directory.Exists(path) && !_fileSystem.File.Exists(path))
        {
            return;
        }

        if (_scannedPaths.Contains(path)) return;
        _scannedPaths.Add(path);

        var isDir = _fileSystem.Directory.Exists(path);
        
        var evidence = new List<Evidence>
        {
            new Evidence(EvidenceType.ExactInstallLocation, "Matches exact installation directory", Name)
        };

        candidates.Add(new ResidualArtifactCandidate(
            new Artifact
            {
                Id = Guid.NewGuid(),
                Path = path,
                Name = _fileSystem.Path.GetFileName(path),
                Type = isDir ? ArtifactType.Directory : ArtifactType.File,
                DiscoveredAt = DateTime.UtcNow
            },
            evidence,
            Name
        ));
    }

    private void ScanTargetedDirectory(string rootPath, string appName, string? publisherName, List<ResidualArtifactCandidate> candidates, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !_fileSystem.Directory.Exists(rootPath)) return;

        try
        {
            var directories = _fileSystem.Directory.GetDirectories(rootPath);
            foreach (var dir in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dirName = _fileSystem.Path.GetFileName(dir);
                var normalizedDirName = NormalizeString(dirName);

                // Check Publisher match
                if (!string.IsNullOrWhiteSpace(publisherName) && normalizedDirName.Equals(publisherName, StringComparison.OrdinalIgnoreCase))
                {
                    // If we found a publisher folder, look inside it for the app folder
                    ScanTargetedDirectory(dir, appName, null, candidates, cancellationToken); // Passing null for publisher so we don't infinitely recurse looking for publishers
                    
                    // We also record the publisher folder itself as a candidate, but with lower weight if it contains multiple apps
                    if (!_scannedPaths.Contains(dir))
                    {
                        _scannedPaths.Add(dir);
                        var evidence = new List<Evidence>
                        {
                            new Evidence(EvidenceType.PublisherDirectoryMatch, $"Directory name matches publisher '{publisherName}' in {rootPath}", Name)
                        };
                        candidates.Add(CreateCandidate(dir, ArtifactType.Directory, evidence));
                    }
                    continue;
                }

                // Check Exact App Name match
                if (normalizedDirName.Equals(appName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_scannedPaths.Contains(dir))
                    {
                        _scannedPaths.Add(dir);
                        var evidence = new List<Evidence>
                        {
                            new Evidence(EvidenceType.ApplicationNameDirectoryMatch, $"Directory name exactly matches application '{appName}' in {rootPath}", Name),
                            new Evidence(EvidenceType.KnownApplicationDataLocation, "Located in standard application data/programs location", Name)
                        };
                        candidates.Add(CreateCandidate(dir, ArtifactType.Directory, evidence));
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogDebug("Access denied to {Path}", rootPath);
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogDebug("Directory not found {Path}", rootPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error scanning {Path}", rootPath);
        }
    }

    private ResidualArtifactCandidate CreateCandidate(string path, ArtifactType type, List<Evidence> evidence)
    {
        return new ResidualArtifactCandidate(
            new Artifact
            {
                Id = Guid.NewGuid(),
                Path = path,
                Name = _fileSystem.Path.GetFileName(path),
                Type = type,
                DiscoveredAt = DateTime.UtcNow
            },
            evidence,
            Name
        );
    }

    private static string NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        
        // Remove spaces, dots, and common suffixes for a stricter normalized match
        return new string(value.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
    }
}
