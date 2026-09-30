using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class PasswordUnlockViewModel : INotifyPropertyChanged
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly AsyncCommand _unlockCommand;
    private string _password = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;

    public PasswordUnlockViewModel(
        IAuthorizationService authorizationService,
        IAvaloniaLocalizationService localization)
    {
        _authorizationService = authorizationService
            ?? throw new ArgumentNullException(nameof(authorizationService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _unlockCommand = new AsyncCommand(UnlockAsync, () => !_isBusy && _password.Length > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Unlocked;

    public string Password
    {
        get => _password;
        set
        {
            if (!SetField(ref _password, value ?? string.Empty)) return;
            Message = string.Empty;
            _unlockCommand.NotifyCanExecuteChanged();
        }
    }

    public string Message
    {
        get => _message;
        private set
        {
            if (!SetField(ref _message, value)) return;
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message.Length > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            _unlockCommand.NotifyCanExecuteChanged();
        }
    }

    public ICommand UnlockCommand => _unlockCommand;

    public void Clear()
    {
        Password = string.Empty;
        Message = string.Empty;
    }

    public async Task UnlockAsync()
    {
        if (IsBusy || Password.Length == 0) return;

        var password = Password;
        Password = string.Empty;
        IsBusy = true;

        try
        {
            var result = await _authorizationService.TryUnlockWithPasswordAsync(password);
            if (result == AuthorizationResult.Success)
            {
                Message = string.Empty;
                Unlocked?.Invoke(this, EventArgs.Empty);
                return;
            }

            Message = result == AuthorizationResult.TooManyAttempts
                ? _localization.GetString(AvaloniaStringKeys.UnlockTooManyAttempts)
                : _localization.GetString(AvaloniaStringKeys.UnlockRejected);
        }
        catch (Exception)
        {
            Message = _localization.GetString(AvaloniaStringKeys.UnlockFailedSafely);
        }
        finally
        {
            password = string.Empty;
            IsBusy = false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
