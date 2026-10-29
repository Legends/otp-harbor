using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class PasswordSetupViewModel : INotifyPropertyChanged
{
    private readonly IAuthorizationService _authorization;
    private readonly IPasswordValidationService _passwordValidation;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly AsyncCommand _configureCommand;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;

    public PasswordSetupViewModel(
        IAuthorizationService authorization,
        IPasswordValidationService passwordValidation,
        IAvaloniaLocalizationService localization)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _passwordValidation = passwordValidation ?? throw new ArgumentNullException(nameof(passwordValidation));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _configureCommand = new AsyncCommand(ConfigureAsync, () => !_isBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Configured;

    public string Password
    {
        get => _password;
        set
        {
            if (!SetField(ref _password, value ?? string.Empty)) return;
            Message = string.Empty;
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (!SetField(ref _confirmPassword, value ?? string.Empty)) return;
            Message = string.Empty;
        }
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            _configureCommand.NotifyCanExecuteChanged();
        }
    }

    public ICommand ConfigureCommand => _configureCommand;

    public async Task ConfigureAsync()
    {
        if (IsBusy) return;

        var password = Password;
        var confirmation = ConfirmPassword;

        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmation))
        {
            Password = string.Empty;
            ConfirmPassword = string.Empty;
            Message = _localization.GetString(AvaloniaStringKeys.PasswordRequired);
            return;
        }

        if (password.Length < _passwordValidation.MinimumLength)
        {
            Password = string.Empty;
            ConfirmPassword = string.Empty;
            Message = string.Format(
                _localization.GetString(AvaloniaStringKeys.PasswordMinimumLength),
                _passwordValidation.MinimumLength);
            return;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Message = _localization.GetString(AvaloniaStringKeys.PasswordMismatch);
            return;
        }

        Password = string.Empty;
        ConfirmPassword = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authorization.ConfigurePasswordAsync(password, confirmation);
            if (result == AuthorizationResult.Success)
            {
                Message = string.Empty;
                Configured?.Invoke(this, EventArgs.Empty);
                return;
            }

            Message = result == AuthorizationResult.ExistingVaultConflict
                ? _localization.GetString(AvaloniaStringKeys.ExistingVaultConflict)
                : _localization.GetString(AvaloniaStringKeys.PasswordSetupFailed);
        }
        catch (Exception)
        {
            Message = _localization.GetString(AvaloniaStringKeys.PasswordSetupFailed);
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
            IsBusy = false;
        }
    }

    public void Clear()
    {
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        Message = string.Empty;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
