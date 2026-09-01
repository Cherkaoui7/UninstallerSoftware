using System;
using System.Collections.Generic;
using Uninstaller.Core.Models;
using Uninstaller.Core.Services;
using Uninstaller.Domain.Entities;

public class Program
{
    public static void Main()
    {
        var rawApp = new RawRegistryApplication { 
            DisplayName = "7-Zip", 
            WindowsInstaller = 1, 
            RegistryKeyName = "7zip-guid", 
            UninstallString = "test.exe" 
        };

        var normalizer = new ApplicationNormalizer();
        var discoveredApp = normalizer.Normalize(rawApp);

        var existingApp = new Application { 
            Id = Guid.NewGuid(), 
            Name = "7-Zip", 
            IsPresent = true, 
            IsWindowsInstaller = true, 
            RegistryKeyName = "7zip-guid", 
            UninstallCommand = "test.exe" 
        };

        Console.WriteLine($"Discovered: Name={discoveredApp.Name}, IsWindowsInstaller={discoveredApp.IsWindowsInstaller}, RegistryKeyName={discoveredApp.RegistryKeyName}");
        Console.WriteLine($"Existing: Name={existingApp.Name}, IsWindowsInstaller={existingApp.IsWindowsInstaller}, RegistryKeyName={existingApp.RegistryKeyName}");

        var deduplicator = new ApplicationDeduplicator();
        var combined = new List<Application> { existingApp, discoveredApp };
        var final = deduplicator.Deduplicate(combined);

        Console.WriteLine($"Final count: {System.Linq.Enumerable.Count(final)} (Should be 1 if they matched!)");
    }
}
