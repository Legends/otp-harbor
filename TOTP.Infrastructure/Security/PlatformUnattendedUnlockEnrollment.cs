using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class PlatformUnattendedUnlockEnrollment : IPlatformUnattendedUnlockEnrollment
{
    private const int VaultKeySize = 32;

    private readonly IAuthorizationEnvelopeStore _envelopeStore;
    private readonly IMasterPasswordService _passwordService;
    private readonly IStoredVaultKeyVerifier _vaultVerifier;
    private readonly IPlatformUnattendedUnlock _unattendedUnlock;
    private readonly IReadOnlyList<IPlatformQuickUnlock> _adapters;
    private readonly ILogger<PlatformUnattendedUnlockEnrollment> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PlatformUnattendedUnlockEnrollment(
        IAuthorizationEnvelopeStore envelopeStore,
        IMasterPasswordService passwordService,
        IStoredVaultKeyVerifier vaultVerifier,
        IPlatformUnattendedUnlock unattendedUnlock,
        IEnumerable<IPlatformQuickUnlock> adapters,
        ILogger<PlatformUnattendedUnlockEnrollment> logger)
    {
        _envelopeStore = envelopeStore ?? throw new ArgumentNullException(nameof(envelopeStore));
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
        _vaultVerifier = vaultVerifier ?? throw new ArgumentNullException(nameof(vaultVerifier));
        _unattendedUnlock = unattendedUnlock ?? throw new ArgumentNullException(nameof(unattendedUnlock));
        _adapters = adapters?.ToArray() ?? throw new ArgumentNullException(nameof(adapters));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result> EnableAsync(
        string recoveryPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryPassword))
            return Fail(
                PlatformQuickUnlockEnrollmentErrorCode.RecoveryPasswordRequired,
                "The recovery password is required.");

        await _lock.WaitAsync(cancellationToken);
        AuthorizationEnvelopeV2? envelope = null;
        AuthorizationEnvelopeV2? updatedEnvelope = null;
        PlatformQuickUnlockWrapperV2? registeredWrapper = null;
        PlatformQuickUnlockWrapperV2? previousWrapper = null;
        byte[]? recoveredKey = null;
        var persisted = false;
        try
        {
            var loaded = await _envelopeStore.LoadAsync(cancellationToken);
            if (loaded.IsFailed)
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.EnvelopeLoadFailed,
                    "The authorization envelope could not be loaded.",
                    loaded.Errors);
            if (loaded.Value is null)
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.NotConfigured,
                    "Password recovery is not configured.");

            envelope = loaded.Value;
            previousWrapper = envelope.QuickUnlockWrapper;
            recoveredKey = await _passwordService.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                recoveryPassword,
                cancellationToken);
            if (recoveredKey is not { Length: VaultKeySize })
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.InvalidRecoveryPassword,
                    "The recovery password is invalid.");

            var verified = await _vaultVerifier.VerifyAsync(recoveredKey, cancellationToken);
            if (verified.IsFailed
                || verified.Value is not VaultKeyVerificationStatus.Verified
                    and not VaultKeyVerificationStatus.VaultNotFound)
            {
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.VaultVerificationFailed,
                    "The recovery key did not verify the vault.",
                    verified.Errors);
            }

            if (await _unattendedUnlock.GetAvailabilityAsync(cancellationToken)
                != PlatformQuickUnlockAvailability.Available)
            {
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.PlatformUnavailable,
                    "Unattended platform unlock is unavailable.");
            }

            var registration = await _unattendedUnlock.RegisterAsync(
                recoveredKey,
                cancellationToken);
            if (registration.IsFailed)
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed,
                    "Unattended platform unlock could not be registered.",
                    registration.Errors);

            registeredWrapper = registration.Value;
            if (!string.Equals(
                    registeredWrapper.Provider,
                    _unattendedUnlock.ProviderId,
                    StringComparison.Ordinal)
                || !PlatformQuickUnlockContract.IsSupportedAndroidUnattendedWrapper(
                    registeredWrapper))
            {
                await RemoveAsync(_unattendedUnlock, registeredWrapper);
                AuthorizationEnvelopeBufferCleaner.Clear(registeredWrapper);
                registeredWrapper = null;
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed,
                    "The platform returned invalid unattended-unlock metadata.");
            }

            updatedEnvelope = envelope with { QuickUnlockWrapper = registeredWrapper };
            var saved = await _envelopeStore.SaveAsync(updatedEnvelope, cancellationToken);
            if (saved.IsFailed)
            {
                await RemoveAsync(_unattendedUnlock, registeredWrapper);
                registeredWrapper = null;
                return Fail(
                    PlatformQuickUnlockEnrollmentErrorCode.PersistenceFailed,
                    "The unattended-unlock registration could not be persisted.",
                    saved.Errors);
            }

            persisted = true;
            if (previousWrapper is not null)
                await RemovePreviousBestEffortAsync(previousWrapper);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (registeredWrapper is not null && !persisted)
                await RemoveAsync(_unattendedUnlock, registeredWrapper);
            throw;
        }
        catch (Exception exception)
        {
            if (registeredWrapper is not null && !persisted)
                await RemoveAsync(_unattendedUnlock, registeredWrapper);
            _logger.LogError(
                exception,
                "Unattended platform-unlock enrollment failed unexpectedly.");
            return Fail(
                PlatformQuickUnlockEnrollmentErrorCode.UnexpectedFailure,
                "Unattended platform unlock could not be configured.",
                exception: exception);
        }
        finally
        {
            if (recoveredKey is not null) CryptographicOperations.ZeroMemory(recoveredKey);
            if (updatedEnvelope is not null)
            {
                if (envelope?.QuickUnlockWrapper is not null)
                    AuthorizationEnvelopeBufferCleaner.Clear(envelope.QuickUnlockWrapper);
                AuthorizationEnvelopeBufferCleaner.Clear(updatedEnvelope);
            }
            else if (envelope is not null)
                AuthorizationEnvelopeBufferCleaner.Clear(envelope);
            _lock.Release();
        }
    }

    public async Task<Result> DisableAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        AuthorizationEnvelopeV2? envelope = null;
        AuthorizationEnvelopeV2? updatedEnvelope = null;
        try
        {
            var loaded = await _envelopeStore.LoadAsync(cancellationToken);
            if (loaded.IsFailed || loaded.Value is null)
                return Result.Fail("The authorization envelope could not be loaded.");

            envelope = loaded.Value;
            var wrapper = envelope.QuickUnlockWrapper;
            if (!PlatformQuickUnlockContract.IsSupportedAndroidUnattendedWrapper(wrapper))
                return Result.Ok();

            updatedEnvelope = envelope with { QuickUnlockWrapper = null };
            var saved = await _envelopeStore.SaveAsync(updatedEnvelope, cancellationToken);
            if (saved.IsFailed) return Result.Fail(saved.Errors);

            var removed = await _unattendedUnlock.RemoveAsync(wrapper!, cancellationToken);
            if (removed.IsFailed)
            {
                _logger.LogWarning(
                    "The unused unattended-unlock platform key could not be removed.");
            }

            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unattended platform-unlock removal failed unexpectedly.");
            return Result.Fail("Unattended platform unlock could not be removed.");
        }
        finally
        {
            if (updatedEnvelope is not null)
            {
                if (envelope?.QuickUnlockWrapper is not null)
                    AuthorizationEnvelopeBufferCleaner.Clear(envelope.QuickUnlockWrapper);
                AuthorizationEnvelopeBufferCleaner.Clear(updatedEnvelope);
            }
            else if (envelope is not null)
                AuthorizationEnvelopeBufferCleaner.Clear(envelope);
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task RemovePreviousBestEffortAsync(PlatformQuickUnlockWrapperV2 wrapper)
    {
        var adapter = _adapters.FirstOrDefault(value => string.Equals(
            value.ProviderId,
            wrapper.Provider,
            StringComparison.Ordinal));
        if (adapter is null || ReferenceEquals(adapter, _unattendedUnlock)) return;
        var removed = await RemoveAsync(adapter, wrapper);
        if (removed.IsFailed)
            _logger.LogWarning("A replaced platform-unlock key could not be removed.");
    }

    private static async Task<Result> RemoveAsync(
        IPlatformQuickUnlock adapter,
        PlatformQuickUnlockWrapperV2 wrapper)
    {
        try
        {
            return await adapter.RemoveAsync(wrapper, CancellationToken.None);
        }
        catch (Exception exception)
        {
            return Result.Fail(new Error("Platform-unlock cleanup failed.").CausedBy(exception));
        }
    }

    private static Result Fail(
        PlatformQuickUnlockEnrollmentErrorCode code,
        string message,
        IEnumerable<IError>? causes = null,
        Exception? exception = null)
    {
        var errors = new List<IError>
        {
            new PlatformQuickUnlockEnrollmentError(code, message, exception)
        };
        if (causes is not null) errors.AddRange(causes);
        return Result.Fail(errors);
    }
}
