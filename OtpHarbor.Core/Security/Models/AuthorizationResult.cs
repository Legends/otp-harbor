namespace TOTP.Core.Security.Models;

public enum AuthorizationResult
{
    Success = 0,
    NotConfigured = 1,
    NotAvailable = 2,
    RequiresUserInput = 3,
    InvalidCredentials = 4,
    Failed = 5,
    Cancelled = 6,
    TooManyAttempts = 7,
    DisabledByPolicy = 8,
    PasswordRequired = 9,
    ExistingVaultConflict = 10
}
