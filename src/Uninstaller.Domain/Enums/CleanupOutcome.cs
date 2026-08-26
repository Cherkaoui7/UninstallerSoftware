namespace Uninstaller.Domain.Enums;

public enum CleanupOutcome
{
    None = 0,
    DeletedAndVerified = 1,
    NotFound = 2,
    ValidationFailed = 3,
    Protected = 4,
    ReparseBlocked = 5,
    OutsideExpectedRoot = 6,
    Locked = 7,
    AccessDenied = 8,
    DirectoryNotEmpty = 9,
    DeleteFailed = 10,
    VerificationFailed = 11,
    Cancelled = 12,
    IdentityMismatch = 13
}
