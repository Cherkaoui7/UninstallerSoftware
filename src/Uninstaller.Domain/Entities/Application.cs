using System;

namespace Uninstaller.Domain.Entities;

public class Application
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? InstallLocation { get; set; }
    public string? UninstallCommand { get; set; }
    public string? QuietUninstallCommand { get; set; }
    public string? Architecture { get; set; }
    public DateTime? InstallDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
