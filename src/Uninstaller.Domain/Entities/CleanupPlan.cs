using System;
using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class CleanupPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UninstallSessionId { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public CleanupPlanStatus Status { get; set; }
    
    public CleanupPlanSummary Summary { get; set; } = new CleanupPlanSummary();
    
    public List<CleanupPlanItem> Items { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
