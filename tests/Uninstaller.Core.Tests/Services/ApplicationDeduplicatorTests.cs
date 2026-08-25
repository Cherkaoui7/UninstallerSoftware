using System;
using System.Collections.Generic;
using System.Linq;
using Uninstaller.Domain.Entities;
using Uninstaller.Core.Services;
using Xunit;

namespace Uninstaller.Core.Tests.Services;

public class ApplicationDeduplicatorTests
{
    private readonly ApplicationDeduplicator _deduplicator;

    public ApplicationDeduplicatorTests()
    {
        _deduplicator = new ApplicationDeduplicator();
    }

    [Fact]
    public void Deduplicate_MergesExactDuplicates()
    {
        var app1 = new Application { Name = "Test App", Publisher = "Test Pub", RegistrySource = "HKLM64" };
        var app2 = new Application { Name = "Test App", Publisher = "Test Pub", RegistrySource = "HKLM32" };
        // Without version, name + publisher isn't enough unless they have exact same install path or uninstall command.
        // Wait, if version is null on both? My logic requires non-null for heuristic 4.
        // Let's add version to both to trigger heuristic 4.
        app1.Version = "1.0";
        app2.Version = "1.0";

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Single(results);
        Assert.Equal("HKLM64, HKLM32", results[0].RegistrySource);
    }

    [Fact]
    public void Deduplicate_DoesNotMergeSameNameDifferentPublisher()
    {
        var app1 = new Application { Name = "Test App", Publisher = "Pub A", Version = "1.0", InstallLocation = "C:\\A" };
        var app2 = new Application { Name = "Test App", Publisher = "Pub B", Version = "1.0", InstallLocation = "C:\\B" };

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Deduplicate_DoesNotMergeSameNameDifferentInstallLocation()
    {
        var app1 = new Application { Name = "Test App", InstallLocation = "C:\\Path1", UninstallCommand = "cmd1" };
        var app2 = new Application { Name = "Test App", InstallLocation = "C:\\Path2", UninstallCommand = "cmd2" };

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Deduplicate_Merges32Bit64BitDuplicates_ViaUninstallCommand()
    {
        var app1 = new Application { Name = "Test App", UninstallCommand = "uninst.exe", RegistrySource = "HKLM64" };
        var app2 = new Application { Name = "Test App", UninstallCommand = "uninst.exe", RegistrySource = "HKLM32" };

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Single(results);
    }

    [Fact]
    public void Deduplicate_MissingPublisher_DoesNotMergeUnlessOtherMatch()
    {
        var app1 = new Application { Name = "Test App", InstallLocation = "C:\\Path1" };
        var app2 = new Application { Name = "Test App", InstallLocation = "C:\\Path2" };

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Deduplicate_MissingInstallLocation_MergesViaWindowsInstaller()
    {
        var app1 = new Application { Name = "App", IsWindowsInstaller = true, RegistryKeyName = "{GUID}" };
        var app2 = new Application { Name = "App 32", IsWindowsInstaller = true, RegistryKeyName = "{GUID}" }; // Even with diff name, MSI GUID matches!

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Single(results);
    }

    [Fact]
    public void Deduplicate_PreservesBestMetadata()
    {
        var app1 = new Application 
        { 
            Name = "App", 
            UninstallCommand = "cmd",
            Publisher = "Pub"
        };
        var app2 = new Application 
        { 
            Name = "App", 
            UninstallCommand = "cmd",
            Version = "1.0",
            EstimatedSize = 1024
        };

        var results = _deduplicator.Deduplicate(new[] { app1, app2 }).ToList();

        Assert.Single(results);
        var merged = results[0];
        Assert.Equal("Pub", merged.Publisher);
        Assert.Equal("1.0", merged.Version);
        Assert.Equal(1024, merged.EstimatedSize);
    }
}
