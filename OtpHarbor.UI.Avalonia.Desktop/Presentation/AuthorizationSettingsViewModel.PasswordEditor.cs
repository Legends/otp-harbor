using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed partial class AuthorizationSettingsViewModel
{
    private readonly AsyncCommand _beginPasswordChangeCommand;
    private readonly AsyncCommand _cancelPasswordChangeCommand;
    private string _currentPassword = string.Empty;
    private bool _isPasswordEditorOpen;

    public ICommand BeginPasswordChangeCommand => _beginPasswordChangeCommand;
    public ICommand CancelPasswordChangeCommand => _cancelPasswordChangeCommand;
    public bool IsPasswordEditorOpen
    {
        get => _isPasswordEditorOpen;
        private set
        {
            if (!SetField(ref _isPasswordEditorOpen, value)) return;
            if (value) _authorization.State.Changed += PasswordEditorAuthorizationStateChanged;
            else _authorization.State.Changed -= PasswordEditorAuthorizationStateChanged;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PasswordPresentation)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPasswordEditorEnabled)));
            NotifyCommands();
        }
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (SetField(ref _currentPassword, value ?? string.Empty)) Notification.Clear();
        }
    }

    public int PasswordMinimumLength => _passwordValidation.MinimumLength;
    public bool IsPasswordEditorEnabled => IsPasswordEditorOpen && !IsBusy;
    public object PasswordPresentation => IsPasswordEditorOpen
        ? new PasswordEditorPresentation(this) : new PasswordSummaryPresentation(this);
    public int PasswordLengthProgress => Math.Min(NewPassword.Length, PasswordMinimumLength);
    public string PasswordMinimumLengthHelp => string.Format(CultureInfo.CurrentCulture,
        _localization.GetString(AvaloniaStringKeys.PasswordMinimumLength), PasswordMinimumLength);
    public string QuickUnlockOptionHelp => _localization.GetString(OperatingSystem.IsWindows()
        ? AvaloniaStringKeys.SecurityQuickUnlockWindowsHelp
        : OperatingSystem.IsMacOS() ? AvaloniaStringKeys.SecurityQuickUnlockMacHelp
        : AvaloniaStringKeys.SecurityQuickUnlockOtherHelp);
    private bool CanEditPassword => !_disposed && !IsBusy && _authorization.State.IsConfigured && _authorization.State.IsUnlocked;

    public Task BeginPasswordChangeAsync()
    {
        if (!CanEditPassword || IsPasswordEditorOpen) return Task.CompletedTask;
        ClearPasswordFields();
        IsPasswordEditorOpen = true;
        return Task.CompletedTask;
    }

    public Task CancelPasswordChangeAsync()
    {
        if (_disposed || IsBusy) return Task.CompletedTask;
        ClearSensitiveInputs();
        return Task.CompletedTask;
    }

    private void ClearPasswordFields()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    private void PasswordEditorAuthorizationStateChanged(object? sender, EventArgs args)
    {
        if (!_authorization.State.IsUnlocked) ClearSensitiveInputs();
        NotifyCommands();
    }

    public sealed record PasswordSummaryPresentation(AuthorizationSettingsViewModel Owner);
    public sealed record PasswordEditorPresentation(AuthorizationSettingsViewModel Owner);
}
