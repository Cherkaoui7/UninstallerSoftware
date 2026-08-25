using System;

namespace Uninstaller.Core.Models;

public class DiscoveryResult
{
    public DateTime DiscoveryStartedAt { get; set; }
    public DateTime? DiscoveryCompletedAt { get; set; }
    public int EntriesInspected { get; set; }
    public int ApplicationsDiscovered { get; set; }
    public int ApplicationsAdded { get; set; }
    public int ApplicationsUpdated { get; set; }
    public int ApplicationsUnchanged { get; set; }
    public int EntriesSkipped { get; set; }
    public int Errors { get; set; }
    public bool Cancelled { get; set; }
}
