using System;
using System.Collections.Generic;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class RecoverySession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CleanupSessionId { get; set; }
    public Guid ApplicationId { get; set; }
    public RecoverySessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<RecoveryItem> Items { get; set; } = new();
}
