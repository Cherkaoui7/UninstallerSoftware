using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Uninstaller.Windows.Registry;
using Xunit;

namespace Uninstaller.Windows.Tests.Registry;

[SupportedOSPlatform("windows")]
public class WindowsRegistryServiceTests
{
    private class MockRegistryProvider : IRegistryProvider
    {
        public Dictionary<string, MockRegistryKey> Keys { get; } = new();
        public bool ThrowOnOpenBase { get; set; }

        public IRegistryKeyWrapper? OpenBaseKey(RegistryHive hive, RegistryView view)
        {
            if (ThrowOnOpenBase) throw new SecurityException("Access denied test");
            var keyId = $"{hive}-{view}";
            if (Keys.TryGetValue(keyId, out var key)) return key;
            
            var newKey = new MockRegistryKey();
            Keys[keyId] = newKey;
            return newKey;
        }
    }

    private class MockRegistryKey : IRegistryKeyWrapper
    {
        public Dictionary<string, MockRegistryKey> SubKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowOnOpen { get; set; }

        public IRegistryKeyWrapper? OpenSubKey(string name, bool writable)
        {
            if (ThrowOnOpen) throw new SecurityException("Access denied test");
            if (SubKeys.TryGetValue(name, out var subKey))
            {
                if (subKey.ThrowOnOpen) throw new SecurityException("Access denied test");
                return subKey;
            }
            return null;
        }

        public string[] GetSubKeyNames() => SubKeys.Keys.ToArray();

        public object? GetValue(string name)
        {
            return Values.TryGetValue(name, out var val) ? val : null;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task GetUninstallEntriesAsync_ReadsNormalValidEntries_HKLM64()
    {
        var provider = new MockRegistryProvider();
        var hklm64 = (MockRegistryKey)provider.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)!;
        var uninstallKey = new MockRegistryKey();
        hklm64.SubKeys[@"Software\Microsoft\Windows\CurrentVersion\Uninstall"] = uninstallKey;

        var appKey = new MockRegistryKey();
        appKey.Values["DisplayName"] = "Test App 64";
        uninstallKey.SubKeys["TestAppId64"] = appKey;

        var service = new WindowsRegistryService(new NullLogger<WindowsRegistryService>(), provider);
        var results = await service.GetUninstallEntriesAsync(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Test App 64", results[0].DisplayName);
        Assert.Equal("TestAppId64", results[0].RegistryKeyName);
    }

    [Fact]
    public async Task GetUninstallEntriesAsync_ReadsNormalValidEntries_HKLM32()
    {
        var provider = new MockRegistryProvider();
        var hklm32 = (MockRegistryKey)provider.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)!;
        var uninstallKey = new MockRegistryKey();
        
        // When using RegistryView.Registry32, Windows maps this path transparently to WOW6432Node. 
        // Our WindowsRegistryService passes the transparent path.
        hklm32.SubKeys[@"Software\Microsoft\Windows\CurrentVersion\Uninstall"] = uninstallKey;

        var appKey = new MockRegistryKey();
        appKey.Values["DisplayName"] = "Test App 32";
        uninstallKey.SubKeys["TestAppId32"] = appKey;

        var service = new WindowsRegistryService(new NullLogger<WindowsRegistryService>(), provider);
        var results = await service.GetUninstallEntriesAsync(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Test App 32", results[0].DisplayName);
        Assert.Equal("TestAppId32", results[0].RegistryKeyName);
    }

    [Fact]
    public async Task GetUninstallEntriesAsync_ReadsNormalValidEntries_HKCU()
    {
        var provider = new MockRegistryProvider();
        var hkcu = (MockRegistryKey)provider.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)!;
        var uninstallKey = new MockRegistryKey();
        hkcu.SubKeys[@"Software\Microsoft\Windows\CurrentVersion\Uninstall"] = uninstallKey;

        var appKey = new MockRegistryKey();
        appKey.Values["DisplayName"] = "Test App CU";
        uninstallKey.SubKeys["TestAppIdCU"] = appKey;

        var service = new WindowsRegistryService(new NullLogger<WindowsRegistryService>(), provider);
        var results = await service.GetUninstallEntriesAsync(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Test App CU", results[0].DisplayName);
        Assert.Equal("TestAppIdCU", results[0].RegistryKeyName);
    }

    [Fact]
    public async Task GetUninstallEntriesAsync_HandlesMissingAndMalformedValues()
    {
        // Arrange
        var provider = new MockRegistryProvider();
        var hklm64 = (MockRegistryKey)provider.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)!;
        var uninstallKey = new MockRegistryKey();
        hklm64.SubKeys[@"Software\Microsoft\Windows\CurrentVersion\Uninstall"] = uninstallKey;

        var appKey = new MockRegistryKey();
        // Missing DisplayName completely
        // Malformed EstimatedSize (string instead of int)
        appKey.Values["EstimatedSize"] = "NotAnInt"; 
        
        // SystemComponent as int string
        appKey.Values["SystemComponent"] = "1";

        uninstallKey.SubKeys["MalformedApp"] = appKey;

        var service = new WindowsRegistryService(new NullLogger<WindowsRegistryService>(), provider);

        // Act
        var results = await service.GetUninstallEntriesAsync(CancellationToken.None);

        // Assert
        Assert.Single(results);
        var res = results[0];
        Assert.Null(res.DisplayName);
        Assert.Null(res.EstimatedSize);
        Assert.Equal(1, res.SystemComponent);
    }

    [Fact]
    public async Task GetUninstallEntriesAsync_HandlesInaccessibleKeysGracefully()
    {
        // Arrange
        var provider = new MockRegistryProvider();
        var hklm64 = (MockRegistryKey)provider.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)!;
        var uninstallKey = new MockRegistryKey();
        hklm64.SubKeys[@"Software\Microsoft\Windows\CurrentVersion\Uninstall"] = uninstallKey;

        var inaccessibleAppKey = new MockRegistryKey();
        inaccessibleAppKey.ThrowOnOpen = true;
        
        uninstallKey.SubKeys["InaccessibleApp"] = inaccessibleAppKey;

        var service = new WindowsRegistryService(new NullLogger<WindowsRegistryService>(), provider);

        // Act
        var results = await service.GetUninstallEntriesAsync(CancellationToken.None);

        // Assert
        Assert.Empty(results); // The key was inaccessible, so no entry should be added, and NO exception thrown!
    }

    [Fact]
    public async Task GetUninstallEntriesAsync_HandlesMissingUninstallKey()
    {
        // Arrange
        var provider = new MockRegistryProvider();
        // No uninstall key added
        var service = new WindowsRegistryService(new NullLogger<WindowsRegistryService>(), provider);

        // Act
        var results = await service.GetUninstallEntriesAsync(CancellationToken.None);

        // Assert
        Assert.Empty(results); 
    }
}
