using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Uninstaller.Core.Abstractions;
using Uninstaller.Core.Models;

namespace Uninstaller.Windows.Registry;

[SupportedOSPlatform("windows")]
public class WindowsRegistryService : IRegistryService
{
    private readonly ILogger<WindowsRegistryService> _logger;
    private readonly IRegistryProvider _registryProvider;

    public WindowsRegistryService(ILogger<WindowsRegistryService> logger)
        : this(logger, new RegistryProvider())
    {
    }

    internal WindowsRegistryService(ILogger<WindowsRegistryService> logger, IRegistryProvider registryProvider)
    {
        _logger = logger;
        _registryProvider = registryProvider;
    }

    public Task<IReadOnlyList<RawRegistryApplication>> GetUninstallEntriesAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => 
        {
            var results = new List<RawRegistryApplication>();
            _logger.LogInformation("Starting application discovery from Windows Registry.");

            // HKLM 64-bit
            DiscoverFromKey(RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", results, cancellationToken);
            
            // HKLM 32-bit (WOW6432Node)
            DiscoverFromKey(RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", results, cancellationToken);
            
            // HKCU (Usually doesn't differ by architecture for uninstall keys, but we use Default)
            DiscoverFromKey(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", results, cancellationToken);

            _logger.LogInformation("Registry discovery completed. Found {Count} total raw entries.", results.Count);
            return (IReadOnlyList<RawRegistryApplication>)results;
        }, cancellationToken);
    }

    private void DiscoverFromKey(RegistryHive hive, RegistryView view, string subKeyName, List<RawRegistryApplication> results, CancellationToken cancellationToken)
    {
        var sourceName = $"{hive}\\{subKeyName} [{view}]";
        _logger.LogInformation("Inspecting registry source: {Source}", sourceName);

        int inspected = 0;
        int skipped = 0;
        int errors = 0;

        try
        {
            using var baseKey = _registryProvider.OpenBaseKey(hive, view);
            if (baseKey == null)
            {
                _logger.LogWarning("Base registry key could not be opened: {Source}", sourceName);
                return;
            }

            using var uninstallKey = baseKey.OpenSubKey(subKeyName, writable: false);

            if (uninstallKey == null)
            {
                _logger.LogWarning("Uninstall registry key not found: {Source}", sourceName);
                return;
            }

            var subKeyNames = uninstallKey.GetSubKeyNames();

            foreach (var appKeyName in subKeyNames)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                inspected++;

                try
                {
                    using var appKey = uninstallKey.OpenSubKey(appKeyName, writable: false);
                    if (appKey == null)
                    {
                        skipped++;
                        continue;
                    }

                    var rawApp = new RawRegistryApplication
                    {
                        RegistrySource = sourceName,
                        RegistryKeyName = appKeyName,
                        DisplayName = appKey.GetValue("DisplayName") as string,
                        DisplayVersion = appKey.GetValue("DisplayVersion") as string,
                        Publisher = appKey.GetValue("Publisher") as string,
                        InstallLocation = appKey.GetValue("InstallLocation") as string,
                        UninstallString = appKey.GetValue("UninstallString") as string,
                        QuietUninstallString = appKey.GetValue("QuietUninstallString") as string,
                        InstallDate = appKey.GetValue("InstallDate") as string,
                        
                        // Handle potential cast errors gracefully for DWORD values
                        EstimatedSize = ParseInt(appKey.GetValue("EstimatedSize")),
                        SystemComponent = ParseInt(appKey.GetValue("SystemComponent")),
                        WindowsInstaller = ParseInt(appKey.GetValue("WindowsInstaller"))
                    };

                    results.Add(rawApp);
                }
                catch (SecurityException ex)
                {
                    errors++;
                    _logger.LogWarning(ex, "Access denied reading registry key: {AppKeyName} in {Source}", appKeyName, sourceName);
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(ex, "Unexpected error reading registry key: {AppKeyName} in {Source}", appKeyName, sourceName);
                }
            }
        }
        catch (SecurityException ex)
        {
            _logger.LogError(ex, "Access denied opening base registry key: {Source}", sourceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening base registry key: {Source}", sourceName);
        }
        finally
        {
            _logger.LogInformation("Completed {Source}. Inspected: {Inspected}, Skipped: {Skipped}, Errors: {Errors}", sourceName, inspected, skipped, errors);
        }
    }

    private int? ParseInt(object? val)
    {
        if (val is int i) return i;
        if (val is string s && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }
}
