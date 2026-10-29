using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IPasswordValidationService
{
    int MinimumLength { get; }
    bool IsValidRequired(string? password);
    bool IsValidNew(string? password);
    bool IsValidNewWithConfirmation(string? password, string? confirmPassword);
    PasswordValidationResult ValidateRequired(string? password, string requiredMessage);
    PasswordValidationResult ValidateNew(string? password, string requiredMessage, string minLengthMessageFormat);
    PasswordValidationResult ValidateNewWithConfirmation(
        string? password,
        string? confirmPassword,
        string requiredMessage,
        string minLengthMessageFormat,
        string confirmRequiredMessage,
        string mismatchMessage);
}
