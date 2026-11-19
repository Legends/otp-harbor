using System.ComponentModel;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed partial class AuthorizationSettingsViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IPlatformUnattendedUnlock? _unattendedUnlock;
    private readonly AsyncCommand _toggleAppLockCommand;
    private bool _isUnattendedUnlockAvailable;

    public event EventHandler? AppLockChanged;
    public ICommand ToggleAppLockCommand => _toggleAppLockCommand;
    public bool IsAppLockEnabled => _settingsService?.Current.AppLockEnabled ?? true;
    public bool RequiresLockAfterAppLockChange { get; private set; }
    public bool IsUnattendedUnlockAvailable => _isUnattendedUnlockAvailable;
    public string AppLockActionText => _localization.GetString(IsAppLockEnabled
        ? AvaloniaStringKeys.DisableAppLock : AvaloniaStringKeys.EnableAppLock);
    public string AppLockDescription => _localization.GetString(!IsAppLockEnabled
        ? AvaloniaStringKeys.AppLockDisabledDescription
        : IsUnattendedUnlockAvailable ? AvaloniaStringKeys.AppLockEnabledDescription
        : AvaloniaStringKeys.AppLockUnavailable);
    private bool CanToggleAppLock => !_disposed && !IsBusy && _settingsService is not null
        && _authorization.State.IsConfigured && _authorization.State.IsUnlocked;

    private async Task RefreshAppLockAvailabilityAsync()
    {
        try
        {
            _isUnattendedUnlockAvailable = _unattendedUnlock is not null
                && await _unattendedUnlock.GetAvailabilityAsync() == PlatformQuickUnlockAvailability.Available;
        }
        catch (Exception) { _isUnattendedUnlockAvailable = false; }
    }

    public async Task ToggleAppLockAsync()
    {
        if (!CanToggleAppLock || (IsAppLockEnabled && !IsUnattendedUnlockAvailable)) return;
        var previouslyEnabled = IsAppLockEnabled;
        var enable = !previouslyEnabled;
        var completed = false;
        RequiresLockAfterAppLockChange = false;
        IsBusy = true;
        Notification.Clear();
        using var operation = new CancellationTokenSource();
        _unlockMethodChangeLifetime = operation;
        _authorization.State.Changed += AuthorizationStateChanged;
        string? password = null;
        try
        {
            var method = previouslyEnabled ? _authorization.State.PreferredUnlockMethod
                : _settingsService!.Current.PreferredUnlockMethod;
            var result = AuthorizationResult.PasswordRequired;
            if (method is PreferredUnlockMethod.PlatformQuickUnlock or PreferredUnlockMethod.PlatformDeviceCredential)
                result = enable
                    ? await _authorization.ReactivateAppLockWithPlatformAsync(method, operation.Token)
                    : await _authorization.SetAppLockEnabledWithPlatformAsync(false, method, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_disposed || !_authorization.State.IsUnlocked) return;
            if (result is AuthorizationResult.PasswordRequired or AuthorizationResult.NotAvailable or AuthorizationResult.DisabledByPolicy)
            {
                password = await _dialogs.PromptForPasswordAsync(new PasswordDialogRequest(
                    _localization.GetString(enable ? AvaloniaStringKeys.EnableAppLock : AvaloniaStringKeys.DisableAppLock),
                    _localization.GetString(enable ? AvaloniaStringKeys.EnableAppLockPasswordPrompt : AvaloniaStringKeys.DisableAppLockWarning),
                    _localization.GetString(AvaloniaStringKeys.Confirm),
                    _localization.GetString(AvaloniaStringKeys.Cancel),
                    _localization.GetString(AvaloniaStringKeys.PasswordRequired),
                    _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed)), operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if (password is null || _disposed || !_authorization.State.IsUnlocked) return;
                // Activation removes unattended access even when Password is the saved method.
                // Verify recovery credentials before allowing that mutation.
                if (enable)
                {
                    result = await _authorization.TryUnlockWithPasswordAsync(password, operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    if (result != AuthorizationResult.Success)
                    {
                        SetMessage(AvaloniaStringKeys.PasswordVerificationFailed, NotificationSeverity.Error);
                        return;
                    }
                }
                result = await _authorization.SetAppLockEnabledAsync(enable, password, operation.Token);
            }
            operation.Token.ThrowIfCancellationRequested();
            if (_disposed || !_authorization.State.IsUnlocked || result == AuthorizationResult.Cancelled) return;
            if (result != AuthorizationResult.Success || IsAppLockEnabled != enable)
                SetMessage(result == AuthorizationResult.InvalidCredentials
                    ? AvaloniaStringKeys.PasswordVerificationFailed : AvaloniaStringKeys.AppLockChangeFailed,
                    NotificationSeverity.Error);
            else
            {
                completed = true;
                SetMessage(enable ? AvaloniaStringKeys.AppLockEnabled : AvaloniaStringKeys.AppLockDisabled,
                    enable ? NotificationSeverity.Success : NotificationSeverity.Warning);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception) when (operation.IsCancellationRequested) { }
        catch (Exception) { SetMessage(AvaloniaStringKeys.AppLockChangeFailed, NotificationSeverity.Error); }
        finally
        {
            password = null;
            _authorization.State.Changed -= AuthorizationStateChanged;
            _unlockMethodChangeLifetime = null;
            IsQuickUnlockEnabled = IsQuickUnlockPreferred();
            IsBusy = false;
            NotifyAppLockState();
            RequiresLockAfterAppLockChange = IsAppLockEnabled && !completed;
            if (!_disposed && previouslyEnabled != IsAppLockEnabled)
                AppLockChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NotifyAppLockState()
    {
        foreach (var property in new[] { nameof(IsAppLockEnabled), nameof(IsUnattendedUnlockAvailable),
                     nameof(AppLockActionText), nameof(AppLockDescription) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        NotifyCommands();
    }
}
