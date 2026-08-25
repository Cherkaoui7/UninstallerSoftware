using System;
using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public class Artifact
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public ArtifactType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public ArtifactClassification Classification { get; set; }
    public bool IsSelected { get; set; }
    public bool IsProtected { get; set; }
    public DateTime DiscoveredAt { get; set; }
}
