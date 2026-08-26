namespace Uninstaller.Domain.Enums;

public enum PreflightValidationOutcome
{
    Authorized,
    Missing,
    Protected,
    ReparseBlocked,
    IdentityMismatch,
    OutsideExpectedRoot,
    StalePlan,
    InvalidPath,
    UnsupportedArtifact,
    AccessDenied,
    ValidationError
}
