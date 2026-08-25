using System;
using Microsoft.Win32;

namespace Uninstaller.Windows.Registry;

internal interface IRegistryProvider
{
    IRegistryKeyWrapper? OpenBaseKey(RegistryHive hive, RegistryView view);
}

internal interface IRegistryKeyWrapper : IDisposable
{
    IRegistryKeyWrapper? OpenSubKey(string name, bool writable);
    string[] GetSubKeyNames();
    object? GetValue(string name);
}

internal class RegistryProvider : IRegistryProvider
{
    public IRegistryKeyWrapper? OpenBaseKey(RegistryHive hive, RegistryView view)
    {
        var key = RegistryKey.OpenBaseKey(hive, view);
        return key != null ? new RegistryKeyWrapper(key) : null;
    }
}

internal class RegistryKeyWrapper : IRegistryKeyWrapper
{
    private readonly RegistryKey _key;

    public RegistryKeyWrapper(RegistryKey key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public IRegistryKeyWrapper? OpenSubKey(string name, bool writable)
    {
        var subKey = _key.OpenSubKey(name, writable);
        return subKey != null ? new RegistryKeyWrapper(subKey) : null;
    }

    public string[] GetSubKeyNames()
    {
        return _key.GetSubKeyNames();
    }

    public object? GetValue(string name)
    {
        return _key.GetValue(name);
    }

    public void Dispose()
    {
        _key.Dispose();
    }
}
