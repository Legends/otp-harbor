using System.Windows.Input;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record SensitiveAuthenticationOption(PreferredUnlockMethod Method, string Label);

public sealed partial class MobileShellViewModel
{
    private readonly MobileAsyncCommand _chooseInitialBiometricCommand;
    private readonly MobileAsyncCommand _chooseInitialDeviceCredentialCommand;
    private readonly MobileAsyncCommand _skipInitialQuickUnlockCommand;
    private readonly MobileAsyncCommand _disableInitialAppLockCommand;
    private IReadOnlyList<SensitiveAuthenticationOption> _authenticationMethods = [];
    private SensitiveAuthenticationOption? _selectedAuthenticationMethod;
    private bool _isInitialQuickUnlockOfferVisible;
    private bool _usesConfiguredAuthenticationMethod;

    public ICommand ChooseInitialBiometricCommand => _chooseInitialBiometricCommand;
    public ICommand ChooseInitialDeviceCredentialCommand => _chooseInitialDeviceCredentialCommand;
    public ICommand SkipInitialQuickUnlockCommand => _skipInitialQuickUnlockCommand;
    public ICommand ChooseInitialMasterPasswordCommand => _skipInitialQuickUnlockCommand;
    public ICommand ChooseInitialNoAppLockCommand => _disableInitialAppLockCommand;
    public IReadOnlyList<SensitiveAuthenticationOption> AuthenticationMethods => _authenticationMethods;
    public bool HasAuthenticationMethodChoice => !_usesConfiguredAuthenticationMethod && _authenticationMethods.Count > 1;
    public bool IsPasswordAuthorizationSelected =>
        _selectedAuthenticationMethod?.Method is null or PreferredUnlockMethod.Password;
    public bool IsPlatformAuthorizationSelected => !IsPasswordAuthorizationSelected;
    public SensitiveAuthenticationOption? SelectedAuthenticationMethod
    {
        get => _selectedAuthenticationMethod;
        set
        {
            if (value is null || !_authenticationMethods.Contains(value)
                || !SetField(ref _selectedAuthenticationMethod, value)) return;
            BiometricRecoveryPassword = string.Empty;
            AppLockRecoveryPassword = string.Empty;
            EditorSecretAuthorizationPassword = string.Empty;
            NotifyAuthenticationSelectionChanged();
        }
    }

    public bool IsInitialQuickUnlockOfferVisible
    {
        get => _isInitialQuickUnlockOfferVisible;
        private set
        {
            if (!SetField(ref _isInitialQuickUnlockOfferVisible, value)) return;
            OnPropertyChanged(nameof(CanHandleSystemBack));
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
            NotifySensitiveAuthenticationCommands();
        }
    }

    public string AuthenticationMethodText => Get(MobileStringKeys.AuthenticationMethod);
    public string DeviceAuthenticationPromptText => Get(MobileStringKeys.DeviceAuthenticationPrompt);
    public string InitialQuickUnlockTitle => Get(MobileStringKeys.InitialAuthenticationTitle);
    public string InitialQuickUnlockDescription => Get(MobileStringKeys.InitialAuthenticationDescription);
    public string InitialBiometricDescription => Get(MobileStringKeys.InitialBiometricDescription);
    public string InitialDeviceLockDescription => Get(MobileStringKeys.InitialDeviceLockDescription);
    public string InitialDeviceLockTitle => Get(MobileStringKeys.InitialDeviceLockTitle);
    public string InitialMasterPasswordDescription => Get(MobileStringKeys.InitialMasterPasswordDescription);
    public string InitialNoAppLockTitle => Get(MobileStringKeys.InitialNoAppLockTitle);
    public string InitialNoAppLockDescription => Get(MobileStringKeys.InitialNoAppLockDescription);
    public string InitialAuthenticationSettingsHint => Get(MobileStringKeys.InitialAuthenticationSettingsHint);
    public string InitialAuthenticationRecommended => Get(MobileStringKeys.InitialAuthenticationRecommended);
    public string InitialAuthenticationDefault => Get(MobileStringKeys.InitialAuthenticationDefault);
    public string NotNowText => Get(MobileStringKeys.NotNow);

    private PreferredUnlockMethod SelectedAuthorizationMethod =>
        _selectedAuthenticationMethod?.Method ?? PreferredUnlockMethod.Password;

    private bool CanConfirmSensitiveAuthorization(string password) =>
        !IsPasswordAuthorizationSelected || password.Length > 0;

    private void SelectPasswordAuthorizationFallback()
    {
        SelectedAuthenticationMethod = _authenticationMethods.FirstOrDefault(option => option.Method == PreferredUnlockMethod.Password);
    }

    private async Task PrepareSensitiveAuthenticationAsync(bool useConfiguredMethod = false)
    {
        await RefreshUnlockMethodAvailabilityAsync();
        _usesConfiguredAuthenticationMethod = useConfiguredMethod;
        var current = IsAppLockEnabled ? _authorization.State.PreferredUnlockMethod
            : useConfiguredMethod ? _settings.Current.PreferredUnlockMethod : PreferredUnlockMethod.Password;
        var methods = new List<PreferredUnlockMethod> { PreferredUnlockMethod.Password };
        var usesConfiguredMethod = IsAppLockEnabled || useConfiguredMethod;
        if (usesConfiguredMethod && current == PreferredUnlockMethod.PlatformQuickUnlock
            || !usesConfiguredMethod && IsBiometricAvailable)
            methods.Add(PreferredUnlockMethod.PlatformQuickUnlock);
        if (usesConfiguredMethod && current == PreferredUnlockMethod.PlatformDeviceCredential
            || !usesConfiguredMethod && IsDeviceCredentialAvailable)
            methods.Add(PreferredUnlockMethod.PlatformDeviceCredential);
        _authenticationMethods = methods.Select(method => new SensitiveAuthenticationOption(method, AuthenticationLabel(method))).ToArray();
        _selectedAuthenticationMethod = _authenticationMethods.FirstOrDefault(option => option.Method == current)
            ?? _authenticationMethods[0];
        BiometricRecoveryPassword = string.Empty;
        AppLockRecoveryPassword = string.Empty;
        EditorSecretAuthorizationPassword = string.Empty;
        NotifyAuthenticationSelectionChanged();
    }

    private string AuthenticationLabel(PreferredUnlockMethod method) => method switch
    {
        PreferredUnlockMethod.PlatformQuickUnlock => UnlockWithBiometricsText,
        PreferredUnlockMethod.PlatformDeviceCredential => UnlockWithDevicePinText,
        _ => MasterPasswordText
    };

    private void RefreshSensitiveAuthenticationTexts()
    {
        var selected = SelectedAuthorizationMethod;
        _authenticationMethods = _authenticationMethods.Select(option =>
            new SensitiveAuthenticationOption(option.Method, AuthenticationLabel(option.Method))).ToArray();
        _selectedAuthenticationMethod = _authenticationMethods.FirstOrDefault(option => option.Method == selected);
        foreach (var name in new[] { nameof(AuthenticationMethodText), nameof(DeviceAuthenticationPromptText),
                     nameof(InitialQuickUnlockTitle), nameof(InitialQuickUnlockDescription), nameof(NotNowText),
                     nameof(InitialBiometricDescription), nameof(InitialDeviceLockDescription), nameof(InitialDeviceLockTitle),
                     nameof(InitialMasterPasswordDescription), nameof(InitialNoAppLockTitle),
                     nameof(InitialNoAppLockDescription), nameof(InitialAuthenticationSettingsHint),
                     nameof(InitialAuthenticationRecommended), nameof(InitialAuthenticationDefault) })
            OnPropertyChanged(name);
        NotifyAuthenticationSelectionChanged();
    }

    private void NotifyAuthenticationSelectionChanged()
    {
        OnPropertyChanged(nameof(AuthenticationMethods));
        OnPropertyChanged(nameof(SelectedAuthenticationMethod));
        OnPropertyChanged(nameof(HasAuthenticationMethodChoice));
        OnPropertyChanged(nameof(IsPasswordAuthorizationSelected));
        OnPropertyChanged(nameof(IsPlatformAuthorizationSelected));
        _enableBiometricCommand?.NotifyCanExecuteChanged();
        _confirmDisableAppLockCommand?.NotifyCanExecuteChanged();
        _confirmEditorSecretAuthorizationCommand?.NotifyCanExecuteChanged();
    }

    private void NotifySensitiveAuthenticationCommands()
    {
        _chooseInitialBiometricCommand?.NotifyCanExecuteChanged();
        _chooseInitialDeviceCredentialCommand?.NotifyCanExecuteChanged();
        _skipInitialQuickUnlockCommand?.NotifyCanExecuteChanged();
        _disableInitialAppLockCommand?.NotifyCanExecuteChanged();
    }

    private async Task CompleteInitialQuickUnlockChoiceAsync(PreferredUnlockMethod? method, bool disableAppLock = false)
    {
        if (!IsInitialQuickUnlockOfferVisible || IsBusy) return;
        if (method == PreferredUnlockMethod.PlatformQuickUnlock && !IsBiometricAvailable
            || method == PreferredUnlockMethod.PlatformDeviceCredential && !IsDeviceCredentialAvailable) return;
        IsInitialQuickUnlockOfferVisible = false;
        IsBusy = true;
        using var operation = BeginSensitiveOperation();
        try
        {
            var result = disableAppLock
                ? await _authorization.CompleteInitialAppLockSetupAsync(InitialAppLockChoice.AppLockDisabled, operation.Token)
                : await _authorization.CompleteInitialQuickUnlockSetupAsync(method, operation.Token);
            if (operation.IsCancellationRequested || !_authorization.State.IsUnlocked) return;
            RefreshUnlockMethodState();
            NotifyAppLockChanged();
            NotifyUnlockMethodChanged();
            if (disableAppLock)
            {
                if (result == AuthorizationResult.Success && !IsAppLockEnabled)
                    SetNotification(Get(MobileStringKeys.AppLockDisabled), NotificationSeverity.Warning);
                else if (result != AuthorizationResult.Cancelled) SetError(MobileStringKeys.AppLockChangeFailed);
            }
            else if (method is not null)
            {
                if (result == AuthorizationResult.Success) SetSuccess(MobileStringKeys.QuickUnlockEnabled);
                else if (result != AuthorizationResult.Cancelled) SetError(MobileStringKeys.QuickUnlockNotEnabled);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception) { SetError(disableAppLock ? MobileStringKeys.AppLockChangeFailed : MobileStringKeys.QuickUnlockNotEnabled); }
        finally
        {
            if (!operation.IsCancellationRequested && !_disposed && _authorization.State.IsUnlocked)
            {
                try { await LoadAccountsAsync(); }
                catch (Exception) { SetError(MobileStringKeys.LoadingAccountsFailed); }
            }
            EndSensitiveOperation(operation);
            IsBusy = false;
        }
    }
}
