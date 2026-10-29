using System;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class PasswordValidationService : IPasswordValidationService
{
    public int MinimumLength => 8;

    public bool IsValidRequired(string? password) =>
        !string.IsNullOrWhiteSpace(password);

    public bool IsValidNew(string? password) =>
        IsValidRequired(password) && password!.Length >= MinimumLength;

    public bool IsValidNewWithConfirmation(string? password, string? confirmPassword) =>
        IsValidNew(password) &&
        !string.IsNullOrWhiteSpace(confirmPassword) &&
        string.Equals(password, confirmPassword, StringComparison.Ordinal);

    public PasswordValidationResult ValidateRequired(string? password, string requiredMessage)
    {
        if (!IsValidRequired(password))
        {
            return new PasswordValidationResult { PasswordError = requiredMessage };
        }

        return new PasswordValidationResult();
    }

    public PasswordValidationResult ValidateNew(string? password, string requiredMessage, string minLengthMessageFormat)
    {
        var requiredValidation = ValidateRequired(password, requiredMessage);
        if (!requiredValidation.IsValid)
        {
            return requiredValidation;
        }

        if (!IsValidNew(password))
        {
            return new PasswordValidationResult
            {
                PasswordError = string.Format(minLengthMessageFormat, MinimumLength)
            };
        }

        return new PasswordValidationResult();
    }

    public PasswordValidationResult ValidateNewWithConfirmation(
        string? password,
        string? confirmPassword,
        string requiredMessage,
        string minLengthMessageFormat,
        string confirmRequiredMessage,
        string mismatchMessage)
    {
        var passwordError = default(string);
        var confirmError = default(string);

        var newPasswordValidation = ValidateNew(password, requiredMessage, minLengthMessageFormat);
        if (!newPasswordValidation.IsValid)
        {
            passwordError = newPasswordValidation.PasswordError;
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            confirmError = confirmRequiredMessage;
        }
        else if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            confirmError = mismatchMessage;
        }

        return new PasswordValidationResult
        {
            PasswordError = passwordError,
            ConfirmPasswordError = confirmError
        };
    }
}
