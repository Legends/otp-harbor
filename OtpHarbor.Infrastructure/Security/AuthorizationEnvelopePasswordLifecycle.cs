using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class AuthorizationEnvelopePasswordLifecycle : IAuthorizationEnvelopePasswordLifecycle
{
    private const int VaultKeySize = 32;

    private readonly IAuthorizationEnvelopeStore _envelopeStore;
    private readonly IMasterPasswordService _passwordService;
    private readonly IPasswordValidationService _passwordValidation;
    private readonly IAuthorizationEnvelopeActivator _activator;
    private readonly ILogger<AuthorizationEnvelopePasswordLifecycle> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AuthorizationEnvelopePasswordLifecycle(
        IAuthorizationEnvelopeStore envelopeStore,
        IMasterPasswordService passwordService,
        IPasswordValidationService passwordValidation,
        IAuthorizationEnvelopeActivator activator,
        ILogger<AuthorizationEnvelopePasswordLifecycle> logger)
    {
        _envelopeStore = envelopeStore ?? throw new ArgumentNullException(nameof(envelopeStore));
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
        _passwordValidation = passwordValidation ?? throw new ArgumentNullException(nameof(passwordValidation));
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SensitiveBuffer>> ConfigureAsync(
        string recoveryPassword,
        CancellationToken cancellationToken = default)
    {
        if (!_passwordValidation.IsValidNew(recoveryPassword))
        {
            return Fail(
                AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidNewPassword,
                "The new recovery password is invalid.");
        }

        await _lock.WaitAsync(cancellationToken);
        AuthorizationEnvelopeV2? existingEnvelope = null;
        AuthorizationEnvelopeV2? proposedEnvelope = null;
        byte[]? candidateKey = null;
        try
        {
            var loaded = await _envelopeStore.LoadAsync(cancellationToken);
            if (loaded.IsFailed)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.EnvelopeLoadFailed,
                    "The authorization envelope could not be loaded.",
                    loaded.Errors);
            }

            existingEnvelope = loaded.Value;
            if (existingEnvelope is not null)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.AlreadyConfigured,
                    "Password recovery is already configured.");
            }

            candidateKey = RandomNumberGenerator.GetBytes(VaultKeySize);
            var passwordWrapper = await _passwordService.WrapKeyV2Async(
                candidateKey,
                recoveryPassword,
                cancellationToken);
            proposedEnvelope = new AuthorizationEnvelopeV2
            {
                PasswordWrapper = passwordWrapper
            };

            var activated = await _activator.ActivateAsync(
                proposedEnvelope,
                candidateKey,
                recoveryPassword,
                cancellationToken);
            if (activated.IsFailed)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.ActivationFailed,
                    "Password recovery could not be configured.",
                    activated.Errors);
            }

            return Result.Ok(SensitiveBuffer.CopyFrom(candidateKey));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password-recovery configuration failed unexpectedly.");
            return Fail(
                AuthorizationEnvelopePasswordLifecycleErrorCode.UnexpectedFailure,
                "Password-recovery configuration failed unexpectedly.",
                exception: ex);
        }
        finally
        {
            if (candidateKey is not null) CryptographicOperations.ZeroMemory(candidateKey);
            if (proposedEnvelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(proposedEnvelope);
            if (existingEnvelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(existingEnvelope);
            _lock.Release();
        }
    }

    public async Task<Result<SensitiveBuffer>> ChangePasswordAsync(
        string currentRecoveryPassword,
        string newRecoveryPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentRecoveryPassword))
        {
            return Fail(
                AuthorizationEnvelopePasswordLifecycleErrorCode.CurrentPasswordRequired,
                "The current recovery password is required.");
        }

        if (!_passwordValidation.IsValidNew(newRecoveryPassword))
        {
            return Fail(
                AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidNewPassword,
                "The new recovery password is invalid.");
        }

        await _lock.WaitAsync(cancellationToken);
        AuthorizationEnvelopeV2? existingEnvelope = null;
        AuthorizationEnvelopeV2? updatedEnvelope = null;
        byte[]? recoveredKey = null;
        try
        {
            var loaded = await _envelopeStore.LoadAsync(cancellationToken);
            if (loaded.IsFailed)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.EnvelopeLoadFailed,
                    "The authorization envelope could not be loaded.",
                    loaded.Errors);
            }

            existingEnvelope = loaded.Value;
            if (existingEnvelope is null)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.NotConfigured,
                    "Password recovery is not configured.");
            }

            recoveredKey = await _passwordService.UnwrapKeyV2Async(
                existingEnvelope.PasswordWrapper,
                currentRecoveryPassword,
                cancellationToken);
            if (recoveredKey is null)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidCurrentPassword,
                    "The current recovery password is invalid.");
            }

            if (recoveredKey.Length != VaultKeySize)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidRecoveredKey,
                    "The current password wrapper returned an invalid vault key.");
            }

            var replacementWrapper = await _passwordService.WrapKeyV2Async(
                recoveredKey,
                newRecoveryPassword,
                cancellationToken);
            updatedEnvelope = existingEnvelope with { PasswordWrapper = replacementWrapper };

            var activated = await _activator.ActivateAsync(
                updatedEnvelope,
                recoveredKey,
                newRecoveryPassword,
                cancellationToken);
            if (activated.IsFailed)
            {
                return Fail(
                    AuthorizationEnvelopePasswordLifecycleErrorCode.ActivationFailed,
                    "The recovery password could not be changed.",
                    activated.Errors);
            }

            return Result.Ok(SensitiveBuffer.CopyFrom(recoveredKey));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery-password replacement failed unexpectedly.");
            return Fail(
                AuthorizationEnvelopePasswordLifecycleErrorCode.UnexpectedFailure,
                "Recovery-password replacement failed unexpectedly.",
                exception: ex);
        }
        finally
        {
            if (recoveredKey is not null) CryptographicOperations.ZeroMemory(recoveredKey);
            if (updatedEnvelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(updatedEnvelope);
            if (existingEnvelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(existingEnvelope);
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private static Result<SensitiveBuffer> Fail(
        AuthorizationEnvelopePasswordLifecycleErrorCode code,
        string message,
        IEnumerable<IError>? causes = null,
        Exception? exception = null)
    {
        var errors = new List<IError>
        {
            new AuthorizationEnvelopePasswordLifecycleError(code, message, exception)
        };
        if (causes is not null) errors.AddRange(causes);
        return Result.Fail<SensitiveBuffer>(errors);
    }
}
