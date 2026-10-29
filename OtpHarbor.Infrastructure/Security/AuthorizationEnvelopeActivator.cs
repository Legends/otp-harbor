using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class AuthorizationEnvelopeActivator : IAuthorizationEnvelopeActivator
{
    private const int VaultKeySize = 32;

    private readonly IMasterPasswordService _passwordService;
    private readonly IStoredVaultKeyVerifier _vaultVerifier;
    private readonly IAuthorizationEnvelopeStore _envelopeStore;
    private readonly ILogger<AuthorizationEnvelopeActivator> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AuthorizationEnvelopeActivator(
        IMasterPasswordService passwordService,
        IStoredVaultKeyVerifier vaultVerifier,
        IAuthorizationEnvelopeStore envelopeStore,
        ILogger<AuthorizationEnvelopeActivator> logger)
    {
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
        _vaultVerifier = vaultVerifier ?? throw new ArgumentNullException(nameof(vaultVerifier));
        _envelopeStore = envelopeStore ?? throw new ArgumentNullException(nameof(envelopeStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result> ActivateAsync(
        AuthorizationEnvelopeV2 envelope,
        ReadOnlyMemory<byte> candidateVaultKey,
        string recoveryPassword,
        CancellationToken cancellationToken = default)
    {
        if (envelope is null)
            return Fail(AuthorizationEnvelopeActivationErrorCode.InvalidEnvelope, "The authorization envelope is required.");
        if (candidateVaultKey.Length != VaultKeySize)
            return Fail(AuthorizationEnvelopeActivationErrorCode.InvalidCandidateKey, "The candidate vault key is invalid.");
        if (string.IsNullOrWhiteSpace(recoveryPassword))
            return Fail(AuthorizationEnvelopeActivationErrorCode.PasswordWrapperRejected, "The recovery password is required.");

        cancellationToken.ThrowIfCancellationRequested();
        var ownedCandidateKey = candidateVaultKey.ToArray();
        byte[]? encodedEnvelope = null;
        AuthorizationEnvelopeV2? verifiedEnvelope = null;
        var lockTaken = false;
        try
        {
            var encoded = AuthorizationEnvelopeV2Codec.Serialize(envelope);
            if (encoded.IsFailed)
                return Fail(
                    AuthorizationEnvelopeActivationErrorCode.InvalidEnvelope,
                    "The authorization envelope is invalid.",
                    encoded.Errors);

            encodedEnvelope = encoded.Value;
            await _lock.WaitAsync(cancellationToken);
            lockTaken = true;

            var decoded = AuthorizationEnvelopeV2Codec.Deserialize(encodedEnvelope);
            CryptographicOperations.ZeroMemory(encodedEnvelope);
            encodedEnvelope = null;
            if (decoded.IsFailed)
                return Fail(
                    AuthorizationEnvelopeActivationErrorCode.InvalidEnvelope,
                    "The authorization envelope could not be verified.",
                    decoded.Errors);

            verifiedEnvelope = decoded.Value;

            byte[]? recoveredKey = null;
            try
            {
                recoveredKey = await _passwordService.UnwrapKeyV2Async(
                    verifiedEnvelope.PasswordWrapper,
                    recoveryPassword,
                    cancellationToken);
                if (recoveredKey is null)
                {
                    return Fail(
                        AuthorizationEnvelopeActivationErrorCode.PasswordWrapperRejected,
                        "The password recovery wrapper could not be opened.");
                }

                if (recoveredKey.Length != VaultKeySize
                    || !CryptographicOperations.FixedTimeEquals(recoveredKey, ownedCandidateKey))
                {
                    return Fail(
                        AuthorizationEnvelopeActivationErrorCode.CandidateKeyMismatch,
                        "The password recovery wrapper does not contain the candidate vault key.");
                }
            }
            finally
            {
                if (recoveredKey is not null) CryptographicOperations.ZeroMemory(recoveredKey);
            }

            var vaultVerification = await _vaultVerifier.VerifyAsync(ownedCandidateKey, cancellationToken);
            if (vaultVerification.IsFailed)
            {
                return Fail(
                    AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed,
                    "The existing vault could not be verified.",
                    vaultVerification.Errors);
            }

            if (vaultVerification.Value is not VaultKeyVerificationStatus.Verified
                and not VaultKeyVerificationStatus.VaultNotFound)
            {
                return Fail(
                    AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed,
                    "The candidate vault key did not verify the existing vault.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var saved = await _envelopeStore.SaveAsync(verifiedEnvelope, cancellationToken);
            return saved.IsSuccess
                ? Result.Ok()
                : Fail(
                    AuthorizationEnvelopeActivationErrorCode.PersistenceFailed,
                    "The verified authorization envelope could not be persisted.",
                    saved.Errors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authorization-envelope activation failed unexpectedly.");
            return Fail(
                AuthorizationEnvelopeActivationErrorCode.UnexpectedFailure,
                "Authorization-envelope activation failed unexpectedly.",
                exception: ex);
        }
        finally
        {
            if (encodedEnvelope is not null) CryptographicOperations.ZeroMemory(encodedEnvelope);
            if (verifiedEnvelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(verifiedEnvelope);
            CryptographicOperations.ZeroMemory(ownedCandidateKey);
            if (lockTaken) _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private static Result Fail(
        AuthorizationEnvelopeActivationErrorCode code,
        string message,
        IEnumerable<IError>? causes = null,
        Exception? exception = null)
    {
        var errors = new List<IError>
        {
            new AuthorizationEnvelopeActivationError(code, message, exception)
        };
        if (causes is not null) errors.AddRange(causes);
        return Result.Fail(errors);
    }
}
