using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class AuthorizationSettingsViewModel : INotifyPropertyChanged
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

    public AuthorizationSettingsViewModel(
        IAuthorizationService authorization,
        IAvaloniaDialogService dialogs,
        IAvaloniaLocalizationService localization,
        IPasswordValidationService passwordValidation,
        TimeSpan? transientMessageDuration = null)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Notification = new NotificationState(transientMessageDuration);
        _localization.CultureChanged += LocalizationCultureChanged;
        _passwordValidation = passwordValidation ?? throw new ArgumentNullException(nameof(passwordValidation));
        _refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        _enableQuickUnlockCommand = new AsyncCommand(
            EnableQuickUnlockAsync,
            () => !IsBusy && IsQuickUnlockAvailable);
        _usePasswordCommand = new AsyncCommand(
            UsePasswordAsync,
            () => !IsBusy);
        _changePasswordCommand = new AsyncCommand(ChangePasswordAsync, () => !IsBusy);
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
        if (IsBusy) return;
        IsBusy = true;
        ShowQuickUnlockRetry = false;
        try
        {
            IsQuickUnlockEnabled = IsQuickUnlockPreferred();
            IsQuickUnlockAvailable = await _authorization.IsHelloAvailableAsync();
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
        }
    }

    public async Task EnableQuickUnlockAsync()
    {
        if (IsBusy || !IsQuickUnlockAvailable || IsQuickUnlockEnabled) return;
        IsBusy = true;
        string? recoveryPassword = null;
        try
        {
            var selected = await _authorization.SetGateAsync(AuthorizationGateKind.Hello);
            if (selected == AuthorizationResult.PasswordRequired)
            {
                recoveryPassword = await _dialogs.PromptForPasswordAsync(CreateRecoveryPasswordRequest());
                if (recoveryPassword is null)
                {
                    SetMessage(AvaloniaStringKeys.QuickUnlockEnrollmentCancelled, NotificationSeverity.Information);
                    return;
                }

                selected = await _authorization.ConfigureHelloAsync(recoveryPassword);
            }

            if (selected == AuthorizationResult.Success)
            {
                IsQuickUnlockEnabled = true;
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
            recoveryPassword = null;
            IsBusy = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQuickUnlockEnabled)));
        }
    }

    public async Task UsePasswordAsync()
    {
        if (IsBusy || !IsQuickUnlockEnabled) return;
        IsBusy = true;
        string? verifiedPassword = null;
        try
        {
            verifiedPassword = await _dialogs.PromptForPasswordAsync(CreateVerificationRequest());
            if (verifiedPassword is null)
            {
                SetMessage(AvaloniaStringKeys.PasswordPreferenceUnchanged, NotificationSeverity.Information);
                return;
            }

            var selected = await _authorization.SetGateAsync(AuthorizationGateKind.Password);
            if (selected == AuthorizationResult.Success)
            {
                IsQuickUnlockEnabled = false;
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
            verifiedPassword = null;
            IsBusy = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQuickUnlockEnabled)));
        }
    }

    public async Task ChangePasswordAsync()
    {
        if (IsBusy) return;

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
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
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
        async (candidate, _) =>
            await _authorization.TryUnlockWithPasswordAsync(candidate) == AuthorizationResult.Success
                ? null
                : _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed));

    private PasswordDialogRequest CreateCurrentPasswordRequest() => new(
        _localization.GetString(AvaloniaStringKeys.ChangeMasterPassword),
        _localization.GetString(AvaloniaStringKeys.CurrentPasswordPrompt),
        _localization.GetString(AvaloniaStringKeys.Confirm),
        _localization.GetString(AvaloniaStringKeys.Cancel),
        _localization.GetString(AvaloniaStringKeys.PasswordRequired),
        _localization.GetString(AvaloniaStringKeys.PasswordVerificationFailed));

    private bool IsQuickUnlockPreferred() =>
        _authorization.State.ConfiguredGate == AuthorizationGateKind.Hello;

    private void SetMessage(string key, NotificationSeverity severity)
    {
        _messageKey = key;
        Notification.ShowForSeverity(_localization.GetString(key), severity);
    }

    private void LocalizationCultureChanged(object? sender, EventArgs e)
    {
        if (_messageKey is not null && Notification.HasMessage)
            Notification.ShowForSeverity(_localization.GetString(_messageKey), Notification.Severity);
    }

    private void NotifyCommands()
    {
        _refreshCommand.NotifyCanExecuteChanged();
        _enableQuickUnlockCommand.NotifyCanExecuteChanged();
        _usePasswordCommand.NotifyCanExecuteChanged();
        _changePasswordCommand.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
