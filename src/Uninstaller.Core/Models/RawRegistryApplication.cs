namespace Uninstaller.Core.Models;

public class RawRegistryApplication
{
    public string RegistrySource { get; set; } = string.Empty;
    public string RegistryKeyName { get; set; } = string.Empty;
    
    public string? DisplayName { get; set; }
    public string? DisplayVersion { get; set; }
    public string? Publisher { get; set; }
    public string? InstallLocation { get; set; }
    public string? UninstallString { get; set; }
    public string? QuietUninstallString { get; set; }
    public string? InstallDate { get; set; }
    
    public int? EstimatedSize { get; set; }
    public int? SystemComponent { get; set; }
    public int? WindowsInstaller { get; set; }
}
