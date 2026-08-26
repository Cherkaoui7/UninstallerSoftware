namespace Uninstaller.Domain.Enums;

public enum EvidenceType
{
    ExactInstallLocation,
    ApplicationNameDirectoryMatch,
    PublisherDirectoryMatch,
    KnownApplicationDataLocation,
    PathUnderPreviousInstallLocation,
    
    // Registry Evidence
    ExactApplicationKeyMatch,
    ExactPublisherKeyMatch,
    KnownApplicationRegistryPath,
    RegistrySourceMatch,
    KnownInstallMetadataAssociation,
    
    // Shortcut Evidence
    ExactShortcutTarget,
    InstallLocationTargetMatch,
    ShortcutNameMatch,
    StartupLocation,
    KnownExecutableTarget,
    BrokenShortcutTarget
}
