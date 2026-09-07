using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

/// <summary>
/// Desktop-facing authorization facade backed exclusively by the portable v2
/// envelope and preferences contracts.
/// </summary>
public sealed class PortableAuthorizationService : IAuthorizationService
{
    private readonly ISettingsService _settingsService;
    private readonly IAuthorizationEnvelopeSession _session;
    private readonly IAuthorizationEnvelopePasswordLifecycle _passwordLifecycle;
    private readonly IPlatformQuickUnlockEnrollment _quickUnlockEnrollment;
    private readonly IPlatformUnattendedUnlockEnrollment _unattendedUnlockEnrollment;
    private readonly IPlatformQuickUnlock _platformQuickUnlock;
    private readonly IPasswordValidationService _passwordValidation;
    private readonly ISecurityContext _securityContext;
    private readonly ILogger<PortableAuthorizationService> _logger;

    public PortableAuthorizationService(
        ISettingsService settingsService,
        IAuthorizationEnvelopeSession session,
        IAuthorizationEnvelopePasswordLifecycle passwordLifecycle,
        IPlatformQuickUnlockEnrollment quickUnlockEnrollment,
        IPlatformQuickUnlock platformQuickUnlock,
        IPasswordValidationService passwordValidation,
        ISecurityContext securityContext,
        AuthorizationState state,
        ILogger<PortableAuthorizationService> logger,
        IPlatformUnattendedUnlockEnrollment? unattendedUnlockEnrollment = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _passwordLifecycle = passwordLifecycle ?? throw new ArgumentNullException(nameof(passwordLifecycle));
        _quickUnlockEnrollment = quickUnlockEnrollment ?? throw new ArgumentNullException(nameof(quickUnlockEnrollment));
        _unattendedUnlockEnrollment = unattendedUnlockEnrollment
            ?? new UnavailableUnattendedUnlockEnrollment();
        _platformQuickUnlock = platformQuickUnlock ?? throw new ArgumentNullException(nameof(platformQuickUnlock));
        _passwordValidation = passwordValidation ?? throw new ArgumentNullException(nameof(passwordValidation));
        _securityContext = securityContext ?? throw new ArgumentNullException(nameof(securityContext));
        State = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AuthorizationState State { get; }

    public async Task InitializeAsync()
    {
        var initialized = await _session.InitializeAsync();
        if (initialized.IsFailed)
        {
            _logger.LogError("Portable authorization session initialization failed.");
            State.SetConfiguration(false, PreferredUnlockMethod.Password);
            return;
        }

        ApplySessionState();
    }

    public async Task<bool> IsHelloAvailableAsync() =>
        await _platformQuickUnlock.GetAvailabilityAsync() == PlatformQuickUnlockAvailability.Available;

    public Task<AuthorizationResult> TryUnlockOnStartupAsync() =>
        TryUnlockOnStartupAsync(CancellationToken.None);

    public async Task<AuthorizationResult> TryUnlockOnStartupAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!State.IsConfigured) return AuthorizationResult.NotConfigured;

        if (!_settingsService.Current.AppLockEnabled)
        {
            if (!_session.State.HasUnattendedUnlock)
                return AuthorizationResult.PasswordRequired;

            var unattendedResult = await TryUnlockWithHelloAsync(ct);
            if (unattendedResult != AuthorizationResult.Success)
                await SetAppLockEnabledAsync(true, string.Empty);
            return unattendedResult;
        }

        return State.PreferredUnlockMethod == PreferredUnlockMethod.PlatformQuickUnlock
            && _session.State.HasQuickUnlock
                ? await TryUnlockWithHelloAsync(ct)
                : AuthorizationResult.PasswordRequired;
    }

    public async Task<AuthorizationResult> TryUnlockWithPasswordAsync(string password)
    {
        var result = await _session.TryUnlockWithPasswordAsync(password);
        return CompleteUnlock(result, projectPasswordFallback: false);
    }

    public Task<AuthorizationResult> TryUnlockWithHelloAsync() =>
        TryUnlockWithHelloAsync(CancellationToken.None);

    public async Task<AuthorizationResult> TryUnlockWithHelloAsync(CancellationToken ct)
    {
        var result = await _session.TryUnlockWithPlatformAsync(ct);
        return CompleteUnlock(result, projectPasswordFallback: true);
    }

    public async Task<AuthorizationResult> ConfigurePasswordAsync(
        string password,
        string confirmPassword)
    {
        if (!_passwordValidation.IsValidNewWithConfirmation(password, confirmPassword))
            return AuthorizationResult.InvalidCredentials;

        var configured = await _passwordLifecycle.ConfigureAsync(password);
        if (configured.IsFailed)
            return MapPasswordLifecycleFailure(configured);

        using var vaultKey = configured.Value;
        if (!await RefreshSessionAsync()) return AuthorizationResult.Failed;

        var previousPreference = _settingsService.Current.PreferredUnlockMethod;
        _settingsService.Current.PreferredUnlockMethod = PreferredUnlockMethod.Password;
        var saved = await _settingsService.SaveAsync();
        if (saved.IsFailed)
        {
            _settingsService.Current.PreferredUnlockMethod = previousPreference;
            ApplySessionState();
            return AuthorizationResult.Failed;
        }

        if (!ActivateContext(vaultKey)) return AuthorizationResult.Failed;
        ApplySessionState();
        State.Unlock();
        return AuthorizationResult.Success;
    }

    public Task<AuthorizationResult> ConfigureHelloAsync() =>
        Task.FromResult(AuthorizationResult.PasswordRequired);

    public async Task<AuthorizationResult> ConfigureHelloAsync(string recoveryPassword)
    {
        var enrolled = await _quickUnlockEnrollment.EnableAsync(recoveryPassword);
        if (enrolled.IsFailed)
            return MapEnrollmentFailure(enrolled);

        if (!await RefreshSessionAsync()) return AuthorizationResult.Failed;

        var previousPreference = _settingsService.Current.PreferredUnlockMethod;
        _settingsService.Current.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        var saved = await _settingsService.SaveAsync();
        if (saved.IsFailed)
        {
            _settingsService.Current.PreferredUnlockMethod = previousPreference;
            ApplySessionState();
            return AuthorizationResult.Failed;
        }

        ApplySessionState();
        return AuthorizationResult.Success;
    }

    public async Task<AuthorizationResult> SetGateAsync(AuthorizationGateKind gate)
    {
        if (!State.IsConfigured) return AuthorizationResult.NotConfigured;

        var preference = gate switch
        {
            AuthorizationGateKind.Password => PreferredUnlockMethod.Password,
            AuthorizationGateKind.Hello when _session.State.HasQuickUnlock =>
                PreferredUnlockMethod.PlatformQuickUnlock,
            AuthorizationGateKind.Hello => (PreferredUnlockMethod?)null,
            _ => null
        };
        if (preference is null)
        {
            return gate == AuthorizationGateKind.Hello
                ? AuthorizationResult.PasswordRequired
                : AuthorizationResult.Failed;
        }

        var previousPreference = _settingsService.Current.PreferredUnlockMethod;
        _settingsService.Current.PreferredUnlockMethod = preference.Value;
        var saved = await _settingsService.SaveAsync();
        if (saved.IsFailed)
        {
            _settingsService.Current.PreferredUnlockMethod = previousPreference;
            return AuthorizationResult.Failed;
        }

        ApplySessionState();
        return AuthorizationResult.Success;
    }

    public async Task<AuthorizationResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword)
    {
        if (!_passwordValidation.IsValidNew(newPassword))
            return AuthorizationResult.InvalidCredentials;

        var changed = await _passwordLifecycle.ChangePasswordAsync(currentPassword, newPassword);
        if (changed.IsFailed)
            return MapPasswordLifecycleFailure(changed);

        using var vaultKey = changed.Value;
        if (!await RefreshSessionAsync()) return AuthorizationResult.Failed;
        if (!ActivateContext(vaultKey)) return AuthorizationResult.Failed;

        ApplySessionState();
        State.Unlock();
        return AuthorizationResult.Success;
    }

    public async Task<AuthorizationResult> SetAppLockEnabledAsync(
        bool enabled,
        string recoveryPassword)
    {
        if (!State.IsConfigured) return AuthorizationResult.NotConfigured;
        if (_settingsService.Current.AppLockEnabled == enabled)
            return AuthorizationResult.Success;

        if (!enabled)
        {
            var enrolled = await _unattendedUnlockEnrollment.EnableAsync(recoveryPassword);
            if (enrolled.IsFailed) return MapEnrollmentFailure(enrolled);

            var previousPreference = _settingsService.Current.PreferredUnlockMethod;
            _settingsService.Current.AppLockEnabled = false;
            _settingsService.Current.PreferredUnlockMethod =
                PreferredUnlockMethod.PlatformQuickUnlock;
            var saved = await _settingsService.SaveAsync();
            if (saved.IsFailed)
            {
                _settingsService.Current.AppLockEnabled = true;
                _settingsService.Current.PreferredUnlockMethod = previousPreference;
                await _unattendedUnlockEnrollment.DisableAsync();
                await RefreshSessionAsync();
                ApplySessionState();
                return AuthorizationResult.Failed;
            }

            if (!await RefreshSessionAsync()) return AuthorizationResult.Failed;
            ApplySessionState();
            return AuthorizationResult.Success;
        }

        _settingsService.Current.AppLockEnabled = true;
        _settingsService.Current.PreferredUnlockMethod = PreferredUnlockMethod.Password;
        var preferenceSaved = await _settingsService.SaveAsync();
        if (preferenceSaved.IsFailed)
        {
            // Fail closed in memory even when persistence is unavailable. The
            // next startup will retry the unattended wrapper and fall back to
            // the password gate if it is still unusable.
            ApplySessionState();
            return AuthorizationResult.Failed;
        }

        var disabled = await _unattendedUnlockEnrollment.DisableAsync();
        if (!await RefreshSessionAsync()) return AuthorizationResult.Failed;
        ApplySessionState();
        return disabled.IsSuccess ? AuthorizationResult.Success : AuthorizationResult.Failed;
    }

    public void Logout() => Lock();

    public void Lock()
    {
        _securityContext.Lock();
        State.Lock();
    }

    private AuthorizationResult CompleteUnlock(
        FluentResults.Result<AuthorizationResult> result,
        bool projectPasswordFallback)
    {
        if (result.IsFailed)
        {
            _logger.LogError("Portable authorization unlock failed.");
            return AuthorizationResult.Failed;
        }

        if (result.Value == AuthorizationResult.Success)
        {
            State.Unlock();
        }
        else if (projectPasswordFallback && result.Value == AuthorizationResult.PasswordRequired)
        {
            State.SetConfiguration(State.IsConfigured, PreferredUnlockMethod.Password);
        }

        return result.Value;
    }

    private async Task<bool> RefreshSessionAsync()
    {
        var initialized = await _session.InitializeAsync();
        if (initialized.IsSuccess) return true;
        _logger.LogError("Portable authorization session refresh failed.");
        return false;
    }

    private void ApplySessionState()
    {
        var preference = _settingsService.Current.PreferredUnlockMethod;
        if (!_settingsService.Current.AppLockEnabled
            && !_session.State.HasUnattendedUnlock)
        {
            _settingsService.Current.AppLockEnabled = true;
            preference = PreferredUnlockMethod.Password;
        }
        if (preference == PreferredUnlockMethod.PlatformQuickUnlock
            && !_session.State.HasQuickUnlock)
        {
            preference = PreferredUnlockMethod.Password;
        }

        State.SetConfiguration(_session.State.IsConfigured, preference);
    }

    private bool ActivateContext(SensitiveBuffer vaultKey)
    {
        var keyCopy = vaultKey.Memory.ToArray();
        try
        {
            _securityContext.SetDek(keyCopy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate the verified vault key.");
            _securityContext.Lock();
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
        }
    }

    private static AuthorizationResult MapPasswordLifecycleFailure(
        FluentResults.Result<SensitiveBuffer> result)
    {
        var error = result.Errors.OfType<AuthorizationEnvelopePasswordLifecycleError>().FirstOrDefault();
        if (error?.Code == AuthorizationEnvelopePasswordLifecycleErrorCode.ActivationFailed
            && result.Errors.OfType<AuthorizationEnvelopeActivationError>()
                .Any(value => value.Code == AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed))
        {
            return AuthorizationResult.ExistingVaultConflict;
        }

        return error?.Code switch
        {
            AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidNewPassword or
            AuthorizationEnvelopePasswordLifecycleErrorCode.CurrentPasswordRequired or
            AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidCurrentPassword =>
                AuthorizationResult.InvalidCredentials,
            AuthorizationEnvelopePasswordLifecycleErrorCode.NotConfigured => AuthorizationResult.NotConfigured,
            _ => AuthorizationResult.Failed
        };
    }

    private static AuthorizationResult MapEnrollmentFailure(FluentResults.Result result)
    {
        var error = result.Errors.OfType<PlatformQuickUnlockEnrollmentError>().FirstOrDefault();
        return error?.Code switch
        {
            PlatformQuickUnlockEnrollmentErrorCode.RecoveryPasswordRequired or
            PlatformQuickUnlockEnrollmentErrorCode.InvalidRecoveryPassword =>
                AuthorizationResult.InvalidCredentials,
            PlatformQuickUnlockEnrollmentErrorCode.NotConfigured => AuthorizationResult.NotConfigured,
            PlatformQuickUnlockEnrollmentErrorCode.PlatformUnavailable => AuthorizationResult.NotAvailable,
            PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed
                when result.Errors.OfType<PlatformQuickUnlockError>()
                    .Any(value => value.Code == PlatformQuickUnlockErrorCode.Cancelled) =>
                AuthorizationResult.Cancelled,
            PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed
                when result.Errors.OfType<PlatformQuickUnlockError>()
                    .Any(value => value.Code == PlatformQuickUnlockErrorCode.DisabledByPolicy) =>
                AuthorizationResult.DisabledByPolicy,
            PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed
                when result.Errors.OfType<PlatformQuickUnlockError>()
                    .Any(value => value.Code == PlatformQuickUnlockErrorCode.RetriesExhausted) =>
                AuthorizationResult.TooManyAttempts,
            _ => AuthorizationResult.Failed
        };
    }

    private sealed class UnavailableUnattendedUnlockEnrollment :
        IPlatformUnattendedUnlockEnrollment
    {
        public Task<FluentResults.Result> EnableAsync(
            string recoveryPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FluentResults.Result.Fail(
                "Unattended unlock is unavailable."));

        public Task<FluentResults.Result> DisableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FluentResults.Result.Ok());

        public void Dispose()
        {
        }
    }
}
