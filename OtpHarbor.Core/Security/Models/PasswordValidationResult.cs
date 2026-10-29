namespace TOTP.Core.Security.Models;

public sealed class PasswordValidationResult
{
    public string? PasswordError { get; init; }
    public string? ConfirmPasswordError { get; init; }

    public bool IsValid => string.IsNullOrWhiteSpace(PasswordError) && string.IsNullOrWhiteSpace(ConfirmPasswordError);
}
