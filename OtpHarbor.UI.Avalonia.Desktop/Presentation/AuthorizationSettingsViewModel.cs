using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed partial class AuthorizationSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAuthorizationService _authorization;
    private readonly IAvaloniaDialogService _dialogs;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly IPasswordValidationService _passwordValidation;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _enableQuickUnlockCommand;
    private readonly AsyncCommand _usePasswordCommand;
    private readonly AsyncCommand _changePasswordCommand;
    private bool _isBusy;
    private bool _isQuickUnlockAvailable;
    private bool _isQuickUnlockEnabled;
    private bool _showQuickUnlockRetry;
    private string? _messageKey;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private CancellationTokenSource? _unlockMethodChangeLifetime;
    private bool _disposed;

    public AuthorizationSettingsViewModel(
        IAuthorizationService authorization,
        IAvaloniaDialogService dialogs,
        IAvaloniaLocalizationService localization,
        IPasswordValidationService passwordValidation,
        TimeSpan? transientMessageDuration = null,
        ISettingsService? settingsService = null,
        IPlatformUnattendedUnlock? unattendedUnlock = null)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Notification = new NotificationState(transientMessageDuration);
        _localization.CultureChanged += LocalizationCultureChanged;
        _passwordValidation = passwordValidation ?? throw new ArgumentNullException(nameof(passwordValidation));
        _refreshCommand = new AsyncCommand(RefreshAsync, () => !_disposed && !IsBusy);
        _enableQuickUnlockCommand = new AsyncCommand(
            EnableQuickUnlockAsync,
            () => CanChangeUnlockMethod && IsQuickUnlockAvailable);
        _usePasswordCommand = new AsyncCommand(
            UsePasswordAsync,
            () => CanChangeUnlockMethod);
        _changePasswordCommand = new AsyncCommand(ChangePasswordAsync, () => !_disposed && !IsBusy);
        _settingsService = settingsService;
        _unattendedUnlock = unattendedUnlock;
        _toggleAppLockCommand = new AsyncCommand(ToggleAppLockAsync, () =>
            CanToggleAppLock && (!IsAppLockEnabled || IsUnattendedUnlockAvailable));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand EnableQuickUnlockCommand => _enableQuickUnlockCommand;
    public ICommand UsePasswordCommand => _usePasswordCommand;
    public ICommand ChangePasswordCommand => _changePasswordCommand;

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (!SetField(ref _newPassword, value ?? string.Empty)) return;
            Notification.Clear();
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (!SetField(ref _confirmPassword, value ?? string.Empty)) return;
            Notification.Clear();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            NotifyCommands();
        }
    }

    public bool IsQuickUnlockAvailable
    {
        get => _isQuickUnlockAvailable;
        private set
        {
            if (!SetField(ref _isQuickUnlockAvailable, value)) return;
            _enableQuickUnlockCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsQuickUnlockEnabled
    {
        get => _isQuickUnlockEnabled;
        private set
        {
            if (!SetField(ref _isQuickUnlockEnabled, value)) return;
            _enableQuickUnlockCommand.NotifyCanExecuteChanged();
            _usePasswordCommand.NotifyCanExecuteChanged();
        }
    }

    public bool ShowQuickUnlockRetry
    {
        get => _showQuickUnlockRetry;
        private set => SetField(ref _showQuickUnlockRetry, value);
    }

    public NotificationState Notification { get; }
    public string Message => Notification.Text;
    public NotificationSeverity MessageSeverity => Notification.Severity;

    public async Task RefreshAsync()
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        ShowQuickUnlockRetry = false;
        try
        {
            await RefreshAppLockAvailabilityAsync();
            IsQuickUnlockEnabled = IsQuickUnlockPreferred();
            var available = await _authorization.IsHelloAvailableAsync();
            if (_disposed) return;
            IsQuickUnlockAvailable = available;
            _messageKey = null;
            Notification.Clear();
        }
        catch (Exception)
        {
            IsQuickUnlockAvailable = false;
            ShowQuickUnlockRetry = true;
            SetMessage(AvaloniaStringKeys.QuickUnlockUnavailable, NotificationSeverity.Warning);
        }
        finally
        {
            IsBusy = false;
            NotifyAppLockState();
        }
    }

    public async Task EnableQuickUnlockAsync()
    {
        if (!CanChangeUnlockMethod || !IsQuickUnlockAvailable || IsQuickUnlockEnabled) return;
        IsBusy = true;
        try
        {
            var selected = await ChangePreferredUnlockMethodAsync(PreferredUnlockMethod.PlatformQuickUnlock);
            if (selected is null || _disposed || !_authorization.State.IsUnlocked) return;
            if (selected == AuthorizationResult.Cancelled)
            {
                SetMessage(AvaloniaStringKeys.QuickUnlockEnrollmentCancelled, NotificationSeverity.Information);
                return;
            }

            if (selected == AuthorizationResult.Success)
            {
                IsQuickUnlockEnabled = IsQuickUnlockPreferred();
                SetMessage(AvaloniaStringKeys.QuickUnlockEnabled, NotificationSeverity.Success);
                return;
            }

            SetMessage(
                selected is AuthorizationResult.NotAvailable or AuthorizationResult.DisabledByPolicy
                    ? AvaloniaStringKeys.QuickUnlockUnavailable
                    : AvaloniaStringKeys.QuickUnlockEnrollmentFailed,
                NotificationSeverity.Error);
        }
        catch (Exception)
        {
            SetMessage(AvaloniaStringKeys.QuickUnlockEnrollmentFailed, NotificationSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQuickUnlockEnabled)));
        }
    }

    public async Task UsePasswordAsync()
    {
        if (!CanChangeUnlockMethod || !IsQuickUnlockEnabled) return;
        IsBusy = true;
        try
        {
            var selected = await ChangePreferredUnlockMethodAsync(PreferredUnlockMethod.Password);
            if (selected is null || _disposed || !_authorization.State.IsUnlocked) return;
            if (selected == AuthorizationResult.Cancelled)
            {
                SetMessage(AvaloniaStringKeys.PasswordPreferenceUnchanged, NotificationSeverity.Information);
                return;
            }

            if (selected == AuthorizationResult.Success)
            {
                IsQuickUnlockEnabled = IsQuickUnlockPreferred();
                SetMessage(AvaloniaStringKeys.PasswordPreferred, NotificationSeverity.Success);
                return;
            }

            SetMessage(AvaloniaStringKeys.PasswordPreferenceFailed, NotificationSeverity.Error);
        }
        catch (Exception)
        {
            SetMessage(AvaloniaStringKeys.PasswordPreferenceFailed, NotificationSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQuickUnlockEnabled)));
        }
    }

    public async Task ChangePasswordAsync()
    {
        if (_disposed || IsBusy) return;

        var newPassword = NewPassword;
        var confirmation = ConfirmPassword;
        ClearSensitiveInputs();
        if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmation))
        {
            SetMessage(AvaloniaStringKeys.PasswordRequired, NotificationSeverity.Error);
            return;
        }

        if (newPassword.Length < _passwordValidation.MinimumLength)
        {
            Notification.ShowTransient(
                string.Format(
                    _localization.GetString(AvaloniaStringKeys.PasswordMinimumLength),
                    _passwordValidation.MinimumLength),
                NotificationSeverity.Error);
            return;
        }

        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
        {
            SetMessage(AvaloniaStringKeys.PasswordMismatch, NotificationSeverity.Error);
            return;
        }

        IsBusy = true;
        string? currentPassword = null;
        try
        {
            currentPassword = await _dialogs.PromptForPasswordAsync(CreateCurrentPasswordRequest());
            if (currentPassword is null)
            {
                SetMessage(AvaloniaStringKeys.PasswordChangeCancelled, NotificationSeverity.Information);
                return;
            }

            var changed = await _authorization.ChangePasswordAsync(currentPassword, newPassword);
            SetMessage(
                changed == AuthorizationResult.Success
                    ? AvaloniaStringKeys.PasswordChanged
                    : changed == AuthorizationResult.InvalidCredentials
                        ? AvaloniaStringKeys.PasswordVerificationFailed
                        : AvaloniaStringKeys.PasswordChangeFailed,
                changed == AuthorizationResult.Success
                    ? NotificationSeverity.Success
                    : NotificationSeverity.Error);
        }
        catch (Exception)
        {
            SetMessage(AvaloniaStringKeys.PasswordChangeFailed, NotificationSeverity.Error);
        }
        finally
        {
            currentPassword = null;
            newPassword = string.Empty;
            confirmation = string.Empty;
            IsBusy = false;
        }
    }

    public void ClearSensitiveInputs()
    {
        _unlockMethodChangeLifetime?.Cancel();
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    private bool CanChangeUnlockMethod => !_disposed && !IsBusy && _authorization.State.IsUnlocked && IsAppLockEnabled;

    private async Task<AuthorizationResult?> ChangePreferredUnlockMethodAsync(PreferredUnlockMethod requested)
    {
        using var operation = new CancellationTokenSource();
        _unlockMethodChangeLifetime = operation;
        var state = _authorization.State;
        state.Changed += AuthorizationStateChanged;
        string? password = null;
        try
        {
            var current = _authorization.State.PreferredUnlockMethod;
            if (current is PreferredUnlockMethod.PlatformQuickUnlock or PreferredUnlockMethod.PlatformDeviceCredential)
            {
                var result = await _authorization.ConfigureUnlockMethodWithPlatformAsync(requested, current, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if (_disposed || !_authorization.State.IsUnlocked) return AuthorizationResult.Cancelled;
                if (result is not (AuthorizationResult.PasswordRequired or AuthorizationResult.NotAvailable
                    or AuthorizationResult.DisabledByPolicy)) return result;
            }

            password = await _dialogs.PromptForPasswordAsync(requested == PreferredUnlockMethod.Password
                ? CreateVerificationRequest() : CreateRecoveryPasswordRequest(), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (password is null || _disposed || !_authorization.State.IsUnlocked) return AuthorizationResult.Cancelled;
            var changed = requested == PreferredUnlockMethod.PlatformQuickUnlock
                ? await _authorization.ConfigureHelloAsync(password)
                : await _authorization.ConfigureUnlockMethodAsync(requested, password);
            operation.Token.ThrowIfCancellationRequested();
            return changed;
        }
        catch (Exception) when (operation.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            password = null;
            state.Changed -= AuthorizationStateChanged;
            _unlockMethodChangeLifetime = null;
        }
    }

    private void AuthorizationStateChanged(object? sender, EventArgs args)
    {
        if (!_authorization.State.IsUnlocked) _unlockMethodChangeLifetime?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unlockMethodChangeLifetime?.Cancel();
        _localization.CultureChanged -= LocalizationCultureChanged;
        ClearSensitiveInputs();
        Notification.Dispose();
    }

    private PasswordDialogRequest CreateRecoveryPasswordRequest() => new(
        _localization.GetString(AvaloniaStringKeys.EnableQuickUnlock),
        _localization.GetString(AvaloniaStringKeys.QuickUnlockRecoveryPrompt),
        _localization.GetString(AvaloniaStringKeys.Enable),
        _localization.GetString(AvaloniaStringKeys.Cancel),
        _localization.GetString(AvaloniaStringKeys.PasswordRequired),
        _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed));

    private PasswordDialogRequest CreateVerificationRequest() => new(
        _localization.GetString(AvaloniaStringKeys.UsePasswordAtStartup),
        _localization.GetString(AvaloniaStringKeys.PasswordPreferencePrompt),
        _localization.GetString(AvaloniaStringKeys.Confirm),
        _localization.GetString(AvaloniaStringKeys.Cancel),
        _localization.GetString(AvaloniaStringKeys.PasswordRequired),
        _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed),
        async (candidate, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _authorization.TryUnlockWithPasswordAsync(candidate);
            cancellationToken.ThrowIfCancellationRequested();
            return result == AuthorizationResult.Success ? null
                : _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed);
        });

    private PasswordDialogRequest CreateCurrentPasswordRequest() => new(
        _localization.GetString(AvaloniaStringKeys.ChangeMasterPassword),
        _localization.GetString(AvaloniaStringKeys.CurrentPasswordPrompt),
        _localization.GetString(AvaloniaStringKeys.Confirm),
        _localization.GetString(AvaloniaStringKeys.Cancel),
        _localization.GetString(AvaloniaStringKeys.PasswordRequired),
        _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed));

    private bool IsQuickUnlockPreferred() =>
        (IsAppLockEnabled ? _authorization.State.PreferredUnlockMethod
            : _settingsService?.Current.PreferredUnlockMethod) is PreferredUnlockMethod.PlatformQuickUnlock
            or PreferredUnlockMethod.PlatformDeviceCredential;

    private void SetMessage(string key, NotificationSeverity severity)
    {
        if (_disposed) return;
        _messageKey = key;
        Notification.ShowForSeverity(_localization.GetString(key), severity);
    }

    private void LocalizationCultureChanged(object? sender, EventArgs e)
    {
        NotifyAppLockState();
        if (_messageKey is not null && Notification.HasMessage)
            Notification.ShowForSeverity(_localization.GetString(_messageKey), Notification.Severity);
    }

    private void NotifyCommands()
    {
        _refreshCommand.NotifyCanExecuteChanged();
        _enableQuickUnlockCommand.NotifyCanExecuteChanged();
        _usePasswordCommand.NotifyCanExecuteChanged();
        _changePasswordCommand.NotifyCanExecuteChanged();
        _toggleAppLockCommand.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
