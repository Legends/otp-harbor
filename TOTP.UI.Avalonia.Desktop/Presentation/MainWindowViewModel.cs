using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Infrastructure.Services;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAvaloniaStartupCoordinator _startupCoordinator;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly ISettingsService? _settingsService;
    private readonly SessionLockPolicyBackgroundService? _sessionLockPolicy;
    private readonly IdleMonitoringBackgroundService? _idleLockPolicy;
    private readonly IUiScheduler? _uiScheduler;
    private readonly IAvaloniaCameraScannerDialogService _cameraScannerDialogs;
    private readonly AsyncCommand _initializeCommand;
    private readonly AsyncCommand _lockCommand;
    private readonly AsyncCommand _showAccountsCommand;
    private readonly AsyncCommand _showToolsCommand;
    private readonly AsyncCommand _showSettingsCommand;
    private readonly AsyncCommand _closeSettingsCommand;
    private readonly AsyncCommand _toggleSearchCommand;
    private readonly AsyncCommand _clearSearchCommand;
    private readonly AsyncCommand _beginAddAccountCommand;
    private readonly AsyncCommand _beginEditAccountCommand;
    private readonly AsyncCommand _deleteAccountCommand;
    private readonly AsyncCommand _scanQrCommand;
    private readonly AsyncCommand _quickUnlockCommand;
    private readonly AsyncCommand _usePasswordFallbackCommand;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isBusy;
    private bool _canRetry;
    private bool _isPasswordUnlockVisible;
    private bool _isQuickUnlockVisible;
    private bool _isQuickUnlockBusy;
    private string _quickUnlockMessage = string.Empty;
    private bool _isPasswordSetupVisible;
    private bool _isShellVisible;
    private bool _isAccountListVisible;
    private bool _isToolsVisible;
    private bool _isSettingsVisible;
    private bool _isSearchVisible;
    private bool _accountsChangedWhileSettingsOpen;
    private bool _automaticUpdateCheckStarted;
    private bool _shutdownPrepared;
    private bool _disposed;

    public MainWindowViewModel(
        IAvaloniaStartupCoordinator startupCoordinator,
        IAuthorizationService authorizationService,
        PasswordUnlockViewModel passwordUnlock,
        PasswordSetupViewModel passwordSetup,
        AccountListViewModel accountList,
        SettingsPageViewModel settingsPage,
        AuthorizationSettingsViewModel authorizationSettings,
        NativeFilePickerViewModel nativeFilePicker,
        CameraScannerViewModel cameraScanner,
        UpdateCheckViewModel updateCheck,
        DiagnosticsViewModel diagnostics,
        IAvaloniaLocalizationService localization,
        IAvaloniaCameraScannerDialogService cameraScannerDialogs,
        ISettingsService? settingsService = null,
        SessionLockPolicyBackgroundService? sessionLockPolicy = null,
        IUiScheduler? uiScheduler = null,
        IdleMonitoringBackgroundService? idleLockPolicy = null)
    {
        _startupCoordinator = startupCoordinator ?? throw new ArgumentNullException(nameof(startupCoordinator));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Notification = new NotificationState();
        Notification.ShowPersistent(
            _localization.GetString(AvaloniaStringKeys.StartingSafely),
            NotificationSeverity.Information);
        PasswordUnlock = passwordUnlock ?? throw new ArgumentNullException(nameof(passwordUnlock));
        PasswordSetup = passwordSetup ?? throw new ArgumentNullException(nameof(passwordSetup));
        AccountList = accountList ?? throw new ArgumentNullException(nameof(accountList));
        AccountList.EnableAutomaticCodeGenerationOnSelection();
        SettingsPage = settingsPage ?? throw new ArgumentNullException(nameof(settingsPage));
        AuthorizationSettings = authorizationSettings ?? throw new ArgumentNullException(nameof(authorizationSettings));
        NativeFilePicker = nativeFilePicker ?? throw new ArgumentNullException(nameof(nativeFilePicker));
        CameraScanner = cameraScanner ?? throw new ArgumentNullException(nameof(cameraScanner));
        UpdateCheck = updateCheck ?? throw new ArgumentNullException(nameof(updateCheck));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _cameraScannerDialogs = cameraScannerDialogs
            ?? throw new ArgumentNullException(nameof(cameraScannerDialogs));
        _settingsService = settingsService;
        _sessionLockPolicy = sessionLockPolicy;
        _idleLockPolicy = idleLockPolicy;
        _uiScheduler = uiScheduler;
        if (_sessionLockPolicy is not null)
            _sessionLockPolicy.ApplicationLocked += OnAutomaticLock;
        if (_idleLockPolicy is not null)
            _idleLockPolicy.ApplicationLocked += OnAutomaticLock;
        PasswordUnlock.Unlocked += OnUnlocked;
        PasswordSetup.Configured += OnConfigured;
        CameraScanner.AccountImported += OnAccountImported;
        NativeFilePicker.AccountsChanged += OnAccountsChanged;
        SettingsPage.SettingsSaved += OnSettingsSaved;
        _initializeCommand = new AsyncCommand(InitializeAsync, () => !_isBusy);
        _lockCommand = new AsyncCommand(
            LockAsync,
            () => _isShellVisible && !_isSettingsVisible);
        _showAccountsCommand = new AsyncCommand(
            ShowAccountsAsync,
            () => _isShellVisible && !_isSettingsVisible && !_isAccountListVisible);
        _showToolsCommand = new AsyncCommand(
            ShowToolsAsync,
            () => _isShellVisible && !_isSettingsVisible && !_isToolsVisible);
        _showSettingsCommand = new AsyncCommand(
            ShowSettingsAsync,
            () => _isShellVisible && !_isSettingsVisible);
        _closeSettingsCommand = new AsyncCommand(
            CloseSettingsAsync,
            () => _isShellVisible && _isSettingsVisible);
        _toggleSearchCommand = new AsyncCommand(
            ToggleSearchAsync,
            CanUseToolbarSearch);
        _clearSearchCommand = new AsyncCommand(
            ClearSearchAsync,
            () => CanInteractWithAccounts() && _isSearchVisible);
        _beginAddAccountCommand = new AsyncCommand(
            AccountList.BeginAddAsync,
            () => CanInteractWithAccounts() && AccountList.BeginAddCommand.CanExecute(null));
        _beginEditAccountCommand = new AsyncCommand(
            AccountList.BeginEditAsync,
            () => CanInteractWithAccounts() && AccountList.BeginEditCommand.CanExecute(null));
        _deleteAccountCommand = new AsyncCommand(
            AccountList.DeleteAccountAsync,
            () => CanInteractWithAccounts() && AccountList.DeleteAccountCommand.CanExecute(null));
        _scanQrCommand = new AsyncCommand(
            ScanQrAsync,
            () => _isShellVisible && !_isSettingsVisible);
        _quickUnlockCommand = new AsyncCommand(
            TryQuickUnlockAsync,
            () => _isQuickUnlockVisible && !_isQuickUnlockBusy);
        _usePasswordFallbackCommand = new AsyncCommand(
            UsePasswordFallbackAsync,
            () => _isQuickUnlockVisible && !_isQuickUnlockBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public NotificationState Notification { get; }

    public string StatusText
    {
        get => Notification.Text;
        private set => Notification.ShowPersistent(value, Notification.Severity);
    }

    public NotificationSeverity StatusSeverity
    {
        get => Notification.Severity;
        private set => Notification.ShowPersistent(Notification.Text, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            _initializeCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanRetry
    {
        get => _canRetry;
        private set => SetField(ref _canRetry, value);
    }

    public ICommand InitializeCommand => _initializeCommand;

    public ICommand LockCommand => _lockCommand;

    public ICommand ShowAccountsCommand => _showAccountsCommand;

    public ICommand ShowToolsCommand => _showToolsCommand;

    public ICommand ShowSettingsCommand => _showSettingsCommand;

    public ICommand CloseSettingsCommand => _closeSettingsCommand;

    public ICommand ToggleSearchCommand => _toggleSearchCommand;

    public ICommand ClearSearchCommand => _clearSearchCommand;

    public ICommand BeginAddAccountCommand => _beginAddAccountCommand;

    public ICommand BeginEditAccountCommand => _beginEditAccountCommand;

    public ICommand DeleteAccountCommand => _deleteAccountCommand;

    public ICommand ScanQrCommand => _scanQrCommand;

    public ICommand QuickUnlockCommand => _quickUnlockCommand;

    public ICommand UsePasswordFallbackCommand => _usePasswordFallbackCommand;

    public PasswordUnlockViewModel PasswordUnlock { get; }

    public PasswordSetupViewModel PasswordSetup { get; }

    public AccountListViewModel AccountList { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public AuthorizationSettingsViewModel AuthorizationSettings { get; }

    public NativeFilePickerViewModel NativeFilePicker { get; }

    public CameraScannerViewModel CameraScanner { get; }

    public UpdateCheckViewModel UpdateCheck { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public bool IsPasswordUnlockVisible
    {
        get => _isPasswordUnlockVisible;
        private set => SetField(ref _isPasswordUnlockVisible, value);
    }

    public bool IsQuickUnlockVisible
    {
        get => _isQuickUnlockVisible;
        private set
        {
            if (!SetField(ref _isQuickUnlockVisible, value)) return;
            NotifyQuickUnlockCommands();
        }
    }

    public bool IsQuickUnlockBusy
    {
        get => _isQuickUnlockBusy;
        private set
        {
            if (!SetField(ref _isQuickUnlockBusy, value)) return;
            NotifyQuickUnlockCommands();
        }
    }

    public string QuickUnlockMessage
    {
        get => _quickUnlockMessage;
        private set => SetField(ref _quickUnlockMessage, value);
    }

    public bool IsPasswordSetupVisible
    {
        get => _isPasswordSetupVisible;
        private set => SetField(ref _isPasswordSetupVisible, value);
    }

    public bool IsShellVisible
    {
        get => _isShellVisible;
        private set
        {
            if (!SetField(ref _isShellVisible, value)) return;
            NotifyShellCommands();
        }
    }

    public bool IsAccountListVisible
    {
        get => _isAccountListVisible;
        private set
        {
            if (!SetField(ref _isAccountListVisible, value)) return;
            _showAccountsCommand.NotifyCanExecuteChanged();
            NotifyModalCommands();
        }
    }

    public bool IsToolsVisible
    {
        get => _isToolsVisible;
        private set
        {
            if (!SetField(ref _isToolsVisible, value)) return;
            _showToolsCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        private set
        {
            if (!SetField(ref _isSettingsVisible, value)) return;
            _showSettingsCommand.NotifyCanExecuteChanged();
            NotifyModalCommands();
        }
    }

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        private set
        {
            if (!SetField(ref _isSearchVisible, value)) return;
            _clearSearchCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task InitializeAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        CanRetry = false;
        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = false;
        QuickUnlockMessage = string.Empty;
        IsPasswordSetupVisible = false;
        IsShellVisible = false;
        IsAccountListVisible = false;
        IsToolsVisible = false;
        IsSettingsVisible = false;
        IsSearchVisible = false;
        StatusText = _localization.GetString(AvaloniaStringKeys.StartingSafely);
        StatusSeverity = NotificationSeverity.Information;

        try
        {
            var outcome = await _startupCoordinator.InitializeAsync(_lifetime.Token);
            (StatusText, CanRetry, StatusSeverity) = outcome switch
            {
                AvaloniaStartupOutcome.ReadyForPasswordSetup =>
                    (_localization.GetString(AvaloniaStringKeys.StartupCreateMasterPassword), false, NotificationSeverity.Information),
                AvaloniaStartupOutcome.ReadyForUnlock =>
                    (_localization.GetString(AvaloniaStringKeys.StartupEnterMasterPassword), false, NotificationSeverity.Information),
                AvaloniaStartupOutcome.ReadyForPasswordFallback =>
                    (_localization.GetString(AvaloniaStringKeys.QuickUnlockFallback), false, NotificationSeverity.Warning),
                AvaloniaStartupOutcome.ReadyUnlocked =>
                    (_localization.GetString(AvaloniaStringKeys.VaultUnlocked), false, NotificationSeverity.Success),
                AvaloniaStartupOutcome.PreferencesUnavailable =>
                    (_localization.GetString(AvaloniaStringKeys.StartupPreferencesUnavailable), true, NotificationSeverity.Warning),
                _ =>
                    (_localization.GetString(AvaloniaStringKeys.StartupFailedSafely), true, NotificationSeverity.Error)
            };
            IsPasswordUnlockVisible = outcome is AvaloniaStartupOutcome.ReadyForUnlock
                or AvaloniaStartupOutcome.ReadyForPasswordFallback;
            IsPasswordSetupVisible = outcome == AvaloniaStartupOutcome.ReadyForPasswordSetup;
            if (outcome == AvaloniaStartupOutcome.ReadyUnlocked)
            {
                EnterAuthorizedShell();
                StartAutomaticUpdateCheck();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            StatusText = _localization.GetString(AvaloniaStringKeys.StartupCancelled);
            StatusSeverity = NotificationSeverity.Information;
        }
        catch (Exception)
        {
            StatusText = _localization.GetString(AvaloniaStringKeys.StartupFailedSafely);
            StatusSeverity = NotificationSeverity.Error;
            CanRetry = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        PrepareForShutdown();
        PasswordUnlock.Unlocked -= OnUnlocked;
        PasswordSetup.Configured -= OnConfigured;
        CameraScanner.AccountImported -= OnAccountImported;
        NativeFilePicker.AccountsChanged -= OnAccountsChanged;
        SettingsPage.SettingsSaved -= OnSettingsSaved;
        if (_sessionLockPolicy is not null)
            _sessionLockPolicy.ApplicationLocked -= OnAutomaticLock;
        if (_idleLockPolicy is not null)
            _idleLockPolicy.ApplicationLocked -= OnAutomaticLock;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Notification.Dispose();
        NativeFilePicker.Dispose();
        CameraScanner.Dispose();
        UpdateCheck.Dispose();
    }

    public void PrepareForShutdown()
    {
        if (_shutdownPrepared) return;
        _shutdownPrepared = true;

        _authorizationService.Lock();
        AccountList.Clear();
        PasswordSetup.Clear();
        AuthorizationSettings.ClearSensitiveInputs();
        CameraScanner.Dismiss();
        AuthorizationSettings.ClearSensitiveInputs();
        IsSettingsVisible = false;
        IsToolsVisible = false;
        IsSearchVisible = false;
        IsShellVisible = false;
        IsAccountListVisible = false;
        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = false;
        IsPasswordSetupVisible = false;
        StatusText = _localization.GetString(AvaloniaStringKeys.ClosingSafely);
        StatusSeverity = NotificationSeverity.Information;
    }

    private void OnUnlocked(object? sender, EventArgs e)
    {
        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = false;
        EnterAuthorizedShell();
        StatusText = _localization.GetString(AvaloniaStringKeys.VaultUnlocked);
        StatusSeverity = NotificationSeverity.Success;
        StartAutomaticUpdateCheck();
    }

    private void OnConfigured(object? sender, EventArgs e)
    {
        IsPasswordSetupVisible = false;
        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = false;
        EnterAuthorizedShell();
        StatusText = _localization.GetString(AvaloniaStringKeys.VaultConfigured);
        StatusSeverity = NotificationSeverity.Success;
        StartAutomaticUpdateCheck();
    }

    private async void OnAccountImported(object? sender, AccountImportedEventArgs e)
    {
        try
        {
            await AccountList.RevealImportedAccountAsync(
                e.AccountId,
                e.Status is QrAccountImportStatus.Added or QrAccountImportStatus.KeptBoth,
                e.Message);
        }
        catch (Exception)
        {
            AccountList.LoadCommand.Execute(null);
        }
    }

    private void OnAccountsChanged(object? sender, EventArgs e) =>
        _accountsChangedWhileSettingsOpen = true;

    private void OnSettingsSaved(object? sender, EventArgs e) =>
        AccountList.NotifySettingsChanged();

    public Task LockAsync()
    {
        _authorizationService.Lock();
        ApplyLockedUiState();
        return Task.CompletedTask;
    }

    private void ApplyLockedUiState()
    {
        AccountList.Clear();
        CameraScanner.Dismiss();
        IsSettingsVisible = false;
        IsToolsVisible = false;
        IsSearchVisible = false;
        IsShellVisible = false;
        IsAccountListVisible = false;
        var quickUnlockPreferred = _authorizationService.State is { } state
            && state.PreferredUnlockMethod == PreferredUnlockMethod.PlatformQuickUnlock;
        IsQuickUnlockVisible = quickUnlockPreferred;
        IsPasswordUnlockVisible = !quickUnlockPreferred;
        IsPasswordSetupVisible = false;
        QuickUnlockMessage = string.Empty;
        StatusText = _localization.GetString(
            quickUnlockPreferred
                ? AvaloniaStringKeys.VaultLockedQuickUnlock
                : AvaloniaStringKeys.VaultLockedPassword);
        StatusSeverity = NotificationSeverity.Information;
    }

    public async Task TryQuickUnlockAsync()
    {
        if (!IsQuickUnlockVisible || IsQuickUnlockBusy) return;

        IsQuickUnlockBusy = true;
        QuickUnlockMessage = string.Empty;
        try
        {
            var result = await _authorizationService.TryUnlockWithHelloAsync(_lifetime.Token);
            if (result == AuthorizationResult.Success && _authorizationService.State.IsUnlocked)
            {
                IsQuickUnlockVisible = false;
                EnterAuthorizedShell();
                StatusText = _localization.GetString(AvaloniaStringKeys.VaultUnlocked);
                StatusSeverity = NotificationSeverity.Success;
                StartAutomaticUpdateCheck();
                return;
            }

            if (result == AuthorizationResult.PasswordRequired
                || _authorizationService.State.PreferredUnlockMethod == PreferredUnlockMethod.Password)
            {
                await UsePasswordFallbackAsync();
                return;
            }

            QuickUnlockMessage = _localization.GetString(
                result == AuthorizationResult.Cancelled
                    ? AvaloniaStringKeys.QuickUnlockCancelled
                    : AvaloniaStringKeys.QuickUnlockFailed);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            QuickUnlockMessage = _localization.GetString(AvaloniaStringKeys.QuickUnlockFailed);
        }
        finally
        {
            IsQuickUnlockBusy = false;
        }
    }

    public Task UsePasswordFallbackAsync()
    {
        if (!IsQuickUnlockVisible) return Task.CompletedTask;
        IsQuickUnlockVisible = false;
        IsPasswordUnlockVisible = true;
        QuickUnlockMessage = string.Empty;
        StatusText = _localization.GetString(AvaloniaStringKeys.QuickUnlockFallback);
        StatusSeverity = NotificationSeverity.Information;
        return Task.CompletedTask;
    }

    public Task HandleWindowMinimizedAsync()
    {
        if (!IsShellVisible || _settingsService?.Current.LockOnMinimize != true)
            return Task.CompletedTask;
        return LockAsync();
    }

    private void OnAutomaticLock(object? sender, EventArgs args)
    {
        if (_uiScheduler is null)
        {
            ApplyLockedUiState();
            return;
        }

        _uiScheduler.Post(ApplyLockedUiState);
    }

    public Task ShowAccountsAsync()
    {
        if (IsShellVisible) SetActivePage(ShellPage.Accounts);
        return Task.CompletedTask;
    }

    public Task ShowToolsAsync()
    {
        if (IsShellVisible) SetActivePage(ShellPage.Tools);
        return Task.CompletedTask;
    }

    public Task ScanQrAsync()
    {
        if (!IsShellVisible || IsSettingsVisible) return Task.CompletedTask;
        return _cameraScannerDialogs.ShowAsync(CameraScanner, _lifetime.Token);
    }

    public async Task ShowSettingsAsync()
    {
        if (!IsShellVisible || IsSettingsVisible) return;
        IsSettingsVisible = true;
        SettingsPage.Reload();
        await AuthorizationSettings.RefreshAsync();
    }

    public async Task CloseSettingsAsync()
    {
        if (!IsShellVisible || !IsSettingsVisible) return;
        AuthorizationSettings.ClearSensitiveInputs();
        if (_accountsChangedWhileSettingsOpen)
        {
            await AccountList.LoadAsync();
            _accountsChangedWhileSettingsOpen = false;
        }
        IsSettingsVisible = false;
    }

    public Task ToggleSearchAsync()
    {
        if (!CanUseToolbarSearch()) return Task.CompletedTask;
        if (!IsAccountListVisible) SetActivePage(ShellPage.Accounts);
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible) AccountList.SearchText = string.Empty;
        return Task.CompletedTask;
    }

    public Task ClearSearchAsync()
    {
        if (!CanInteractWithAccounts() || !IsSearchVisible) return Task.CompletedTask;
        if (AccountList.HasSearchText)
            AccountList.SearchText = string.Empty;
        else
            IsSearchVisible = false;
        return Task.CompletedTask;
    }

    private void SetActivePage(ShellPage page)
    {
        if (IsAccountListVisible && page != ShellPage.Accounts)
            AccountList.ClearSensitiveOutput();
        if (IsToolsVisible && page != ShellPage.Tools)
            CameraScanner.Clear();
        if (page != ShellPage.Accounts)
        {
            IsSearchVisible = false;
            AccountList.SearchText = string.Empty;
        }

        IsAccountListVisible = page == ShellPage.Accounts;
        IsToolsVisible = page == ShellPage.Tools;
        if (IsAccountListVisible)
            AccountList.ResumeRowCodeGeneration();
    }

    private void EnterAuthorizedShell()
    {
        IsShellVisible = true;
        SetActivePage(ShellPage.Accounts);
        AccountList.LoadCommand.Execute(null);
    }

    private void StartAutomaticUpdateCheck()
    {
        if (_disposed) return;
        if (_automaticUpdateCheckStarted)
        {
            ShowAvailableUpdateNotification();
            return;
        }
        _automaticUpdateCheckStarted = true;
        _ = CheckForAvailableUpdateAsync();
    }

    private async Task CheckForAvailableUpdateAsync()
    {
        try
        {
            await UpdateCheck.CheckAsync();
            ShowAvailableUpdateNotification();
        }
        catch (Exception)
        {
            // UpdateCheckAsync is best-effort and must never disturb vault use or expose transport details.
        }
    }

    private void ShowAvailableUpdateNotification()
    {
        if (_disposed || !IsShellVisible || !UpdateCheck.HasOffer) return;

        StatusText = string.Format(
            _localization.GetString(AvaloniaStringKeys.UpdateAvailableOnStartup),
            UpdateCheck.Version);
        StatusSeverity = NotificationSeverity.Success;
    }

    private void NotifyShellCommands()
    {
        _lockCommand.NotifyCanExecuteChanged();
        _showAccountsCommand.NotifyCanExecuteChanged();
        _showToolsCommand.NotifyCanExecuteChanged();
        _showSettingsCommand.NotifyCanExecuteChanged();
        NotifyModalCommands();
    }

    private bool CanInteractWithAccounts() =>
        _isShellVisible && _isAccountListVisible && !_isSettingsVisible;

    private bool CanUseToolbarSearch() =>
        _isShellVisible && !_isSettingsVisible;

    private void NotifyModalCommands()
    {
        _lockCommand.NotifyCanExecuteChanged();
        _showAccountsCommand.NotifyCanExecuteChanged();
        _showToolsCommand.NotifyCanExecuteChanged();
        _closeSettingsCommand?.NotifyCanExecuteChanged();
        _toggleSearchCommand?.NotifyCanExecuteChanged();
        _clearSearchCommand?.NotifyCanExecuteChanged();
        _beginAddAccountCommand?.NotifyCanExecuteChanged();
        _beginEditAccountCommand?.NotifyCanExecuteChanged();
        _deleteAccountCommand?.NotifyCanExecuteChanged();
        _scanQrCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyQuickUnlockCommands()
    {
        _quickUnlockCommand?.NotifyCanExecuteChanged();
        _usePasswordFallbackCommand?.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
