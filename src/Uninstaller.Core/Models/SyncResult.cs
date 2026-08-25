using System;

namespace Uninstaller.Core.Models;

public class SyncResult
{
    public int ApplicationsAdded { get; set; }
    public int ApplicationsUpdated { get; set; }
    public int ApplicationsUnchanged { get; set; }
}
