using System;
using System.Collections.Generic;
using System.Linq;
using Uninstaller.Domain.Entities;

namespace Uninstaller.Core.Services;

public class ApplicationDeduplicator : IApplicationDeduplicator
{
    public IEnumerable<Application> Deduplicate(IEnumerable<Application> applications)
    {
        var merged = new List<Application>();

        foreach (var app in applications)
        {
            var existing = FindDuplicate(merged, app);
            if (existing != null)
            {
                Merge(existing, app);
            }
            else
            {
                // We create a deep clone or just add the reference since it's in-memory.
                // We'll just add the reference, but we are mutating it if we merge later.
                merged.Add(app);
            }
        }

        return merged;
    }

    private Application? FindDuplicate(List<Application> merged, Application app)
    {
        foreach (var existing in merged)
        {
            if (AreDuplicates(existing, app))
            {
                return existing;
            }
        }
        return null;
    }

    private bool AreDuplicates(Application a, Application b)
    {
        // 1. Windows Installer exact match (GUID match)
        if (a.IsWindowsInstaller && b.IsWindowsInstaller && 
            !string.IsNullOrEmpty(a.RegistryKeyName) && 
            a.RegistryKeyName.Equals(b.RegistryKeyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Names must match for all other heuristics
        if (!string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2. Exact match of Uninstall Command
        if (!string.IsNullOrEmpty(a.UninstallCommand) &&
            string.Equals(a.UninstallCommand, b.UninstallCommand, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Exact match of Install Location
        if (!string.IsNullOrEmpty(a.InstallLocation) &&
            string.Equals(a.InstallLocation, b.InstallLocation, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 4. Same Name, Same Publisher, Same Version (common for 32/64 bit mirror entries in registry)
        if (!string.IsNullOrEmpty(a.Publisher) && string.Equals(a.Publisher, b.Publisher, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(a.Version) && string.Equals(a.Version, b.Version, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void Merge(Application target, Application source)
    {
        target.Version ??= source.Version;
        target.Publisher ??= source.Publisher;
        target.InstallLocation ??= source.InstallLocation;
        target.UninstallCommand ??= source.UninstallCommand;
        target.QuietUninstallCommand ??= source.QuietUninstallCommand;
        target.EstimatedSize ??= source.EstimatedSize;
        target.InstallDate ??= source.InstallDate;

        target.IsSystemComponent = target.IsSystemComponent || source.IsSystemComponent;
        target.IsWindowsInstaller = target.IsWindowsInstaller || source.IsWindowsInstaller;

        // DB tracking fields
        target.IsPresent = target.IsPresent || source.IsPresent;
        if (source.LastSeen > target.LastSeen) target.LastSeen = source.LastSeen;
        if (source.UpdatedAt > target.UpdatedAt) target.UpdatedAt = source.UpdatedAt;

        // Merge registry provenance
        if (!string.IsNullOrEmpty(source.RegistrySource) && !target.RegistrySource.Contains(source.RegistrySource, StringComparison.OrdinalIgnoreCase))
        {
            target.RegistrySource = string.IsNullOrEmpty(target.RegistrySource) 
                ? source.RegistrySource 
                : $"{target.RegistrySource}, {source.RegistrySource}";
        }
    }
}
