using Uninstaller.Domain.Enums;

namespace Uninstaller.Domain.Entities;

public record Evidence(EvidenceType Type, string Description, string Source);
