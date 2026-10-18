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
    private readonly AsyncCommand _previousOnboardingCommand;
    private readonly AsyncCommand _nextOnboardingCommand;
    private readonly AsyncCommand _finishOnboardingCommand;
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
    private readonly AsyncCommand _returnToQuickUnlockCommand;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IReadOnlyList<NotificationState> _settingsNotificationSources;
    private bool _isBusy;
    private bool _canRetry;
    private bool _isPasswordUnlockVisible;
    private bool _isQuickUnlockVisible;
    private bool _isQuickUnlockBusy;
    private string _quickUnlockMessage = string.Empty;
    private bool _isOnboardingVisible;
    private int _onboardingSlideIndex;
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
    private int _selectedSettingsTabIndex;
    private ShellResumeState? _resumeState;
    private readonly OnboardingPreviewOptions _onboardingPreviewOptions;
    private OnboardingPreviewResumeState? _onboardingPreviewResumeState;

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
        IdleMonitoringBackgroundService? idleLockPolicy = null,
        TimeSpan? settingsNotificationDuration = null,
        OnboardingPreviewOptions? onboardingPreviewOptions = null)
    {
        _startupCoordinator = startupCoordinator ?? throw new ArgumentNullException(nameof(startupCoordinator));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        Notification = new NotificationState();
        SettingsNotification = new NotificationState(settingsNotificationDuration);
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
        _settingsNotificationSources =
        [
            SettingsPage.SettingsNotification,
            SettingsPage.LogFolderNotification,
            AuthorizationSettings.Notification,
            NativeFilePicker.Notification,
            UpdateCheck.Notification,
            Diagnostics.Notification
        ];
        foreach (var source in _settingsNotificationSources.Distinct())
            source.Shown += SettingsNotificationShown;
        _cameraScannerDialogs = cameraScannerDialogs
            ?? throw new ArgumentNullException(nameof(cameraScannerDialogs));
        _settingsService = settingsService;
        _sessionLockPolicy = sessionLockPolicy;
        _idleLockPolicy = idleLockPolicy;
        _uiScheduler = uiScheduler;
        _onboardingPreviewOptions = onboardingPreviewOptions
            ?? new OnboardingPreviewOptions(false);
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
        _previousOnboardingCommand = new AsyncCommand(
            PreviousOnboardingAsync,
            () => IsOnboardingVisible && !IsFirstOnboardingSlide);
        _nextOnboardingCommand = new AsyncCommand(
            NextOnboardingAsync,
            () => IsOnboardingVisible && !IsLastOnboardingSlide);
        _finishOnboardingCommand = new AsyncCommand(
            FinishOnboardingAsync,
            () => IsOnboardingVisible);
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
        _returnToQuickUnlockCommand = new AsyncCommand(
            ReturnToQuickUnlockAsync,
            CanReturnToQuickUnlock);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public NotificationState Notification { get; }

    public NotificationState SettingsNotification { get; }

    public int SelectedSettingsTabIndex
    {
        get => _selectedSettingsTabIndex;
        set
        {
            if (!SetField(ref _selectedSettingsTabIndex, value)) return;
            ClearSettingsNotifications();
        }
    }

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

    public ICommand PreviousOnboardingCommand => _previousOnboardingCommand;

    public ICommand NextOnboardingCommand => _nextOnboardingCommand;

    public ICommand FinishOnboardingCommand => _finishOnboardingCommand;

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

    public ICommand ReturnToQuickUnlockCommand => _returnToQuickUnlockCommand;

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
        private set
        {
            if (!SetField(ref _isPasswordUnlockVisible, value)) return;
            OnPropertyChanged(nameof(IsReturnToQuickUnlockVisible));
            NotifyQuickUnlockCommands();
        }
    }

    public bool IsReturnToQuickUnlockVisible => CanReturnToQuickUnlock();

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
            OnPropertyChanged(nameof(IsReturnToQuickUnlockVisible));
            NotifyQuickUnlockCommands();
        }
    }

    public string QuickUnlockMessage
    {
        get => _quickUnlockMessage;
        private set => SetField(ref _quickUnlockMessage, value);
    }

    public bool IsOnboardingVisible
    {
        get => _isOnboardingVisible;
        private set
        {
            if (!SetField(ref _isOnboardingVisible, value)) return;
            NotifyOnboardingCommands();
        }
    }

    public int OnboardingSlideIndex
    {
        get => _onboardingSlideIndex;
        private set
        {
            var normalized = Math.Clamp(value, 0, 4);
            if (!SetField(ref _onboardingSlideIndex, normalized)) return;
            OnPropertyChanged(nameof(IsOnboardingAddAccountsVisible));
            OnPropertyChanged(nameof(IsOnboardingMoveAccountsVisible));
            OnPropertyChanged(nameof(IsOnboardingOrganizeAccountsVisible));
            OnPropertyChanged(nameof(IsOnboardingIconPackVisible));
            OnPropertyChanged(nameof(IsOnboardingQuickUnlockVisible));
            OnPropertyChanged(nameof(IsFirstOnboardingSlide));
            OnPropertyChanged(nameof(IsLastOnboardingSlide));
            NotifyOnboardingCommands();
        }
    }

    public bool IsOnboardingAddAccountsVisible => OnboardingSlideIndex == 0;
    public bool IsOnboardingMoveAccountsVisible => OnboardingSlideIndex == 1;
    public bool IsOnboardingOrganizeAccountsVisible => OnboardingSlideIndex == 2;
    public bool IsOnboardingIconPackVisible => OnboardingSlideIndex == 3;
    public bool IsOnboardingQuickUnlockVisible => OnboardingSlideIndex == 4;
    public bool IsFirstOnboardingSlide => OnboardingSlideIndex == 0;
    public bool IsLastOnboardingSlide => OnboardingSlideIndex == 4;
    public bool IsOnboardingPreview => _onboardingPreviewResumeState is not null;

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
        IsOnboardingVisible = false;
        OnboardingSlideIndex = 0;
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
            await NativeFilePicker.InitializeAutomaticBackupsAsync();
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
            IsOnboardingVisible = outcome == AvaloniaStartupOutcome.ReadyForPasswordSetup;
            IsPasswordSetupVisible = false;
            if (outcome == AvaloniaStartupOutcome.ReadyUnlocked)
            {
                await EnterAuthorizedShellAsync();
                StartAutomaticUpdateCheck();
            }
            if (_onboardingPreviewOptions.ShowOnStartup
                && outcome is AvaloniaStartupOutcome.ReadyForUnlock
                    or AvaloniaStartupOutcome.ReadyForPasswordFallback
                    or AvaloniaStartupOutcome.ReadyUnlocked)
            {
                ShowOnboardingPreview();
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
        foreach (var source in _settingsNotificationSources.Distinct())
            source.Shown -= SettingsNotificationShown;
        if (_sessionLockPolicy is not null)
            _sessionLockPolicy.ApplicationLocked -= OnAutomaticLock;
        if (_idleLockPolicy is not null)
            _idleLockPolicy.ApplicationLocked -= OnAutomaticLock;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Notification.Dispose();
        SettingsNotification.Dispose();
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

    private async void OnUnlocked(object? sender, EventArgs e)
    {
        await EnterAuthorizedShellAsync();
        StatusText = _localization.GetString(AvaloniaStringKeys.VaultUnlocked);
        StatusSeverity = NotificationSeverity.Success;
        StartAutomaticUpdateCheck();
    }

    private async void OnConfigured(object? sender, EventArgs e)
    {
        IsOnboardingVisible = false;
        await EnterAuthorizedShellAsync();
        StatusText = _localization.GetString(AvaloniaStringKeys.VaultConfigured);
        StatusSeverity = NotificationSeverity.Success;
        StartAutomaticUpdateCheck();
    }

    private async void OnAccountImported(object? sender, AccountImportedEventArgs e)
    {
        try
        {
            if (e.Status == QrAccountImportStatus.DuplicateUnchanged)
            {
                AccountList.ShowQrImportOutcome(e.Message, NotificationSeverity.Information);
                return;
            }

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

    public Task PreviousOnboardingAsync()
    {
        if (IsOnboardingVisible && !IsFirstOnboardingSlide)
            OnboardingSlideIndex--;
        return Task.CompletedTask;
    }

    public Task NextOnboardingAsync()
    {
        if (IsOnboardingVisible && !IsLastOnboardingSlide)
            OnboardingSlideIndex++;
        return Task.CompletedTask;
    }

    public Task FinishOnboardingAsync()
    {
        if (!IsOnboardingVisible) return Task.CompletedTask;
        IsOnboardingVisible = false;
        if (_onboardingPreviewResumeState is { } resumeState)
        {
            _onboardingPreviewResumeState = null;
            OnPropertyChanged(nameof(IsOnboardingPreview));
            IsPasswordUnlockVisible = resumeState.PasswordUnlockVisible;
            IsQuickUnlockVisible = resumeState.QuickUnlockVisible;
            IsPasswordSetupVisible = resumeState.PasswordSetupVisible;
            IsShellVisible = resumeState.ShellVisible;
            IsAccountListVisible = resumeState.AccountListVisible;
            IsToolsVisible = resumeState.ToolsVisible;
            IsSettingsVisible = resumeState.SettingsVisible;
            IsSearchVisible = resumeState.SearchVisible;
            StatusText = resumeState.StatusText;
            StatusSeverity = resumeState.StatusSeverity;
            return Task.CompletedTask;
        }
        IsPasswordSetupVisible = true;
        return Task.CompletedTask;
    }

    private void ShowOnboardingPreview()
    {
        _onboardingPreviewResumeState = new OnboardingPreviewResumeState(
            IsPasswordUnlockVisible,
            IsQuickUnlockVisible,
            IsPasswordSetupVisible,
            IsShellVisible,
            IsAccountListVisible,
            IsToolsVisible,
            IsSettingsVisible,
            IsSearchVisible,
            StatusText,
            StatusSeverity);
        OnPropertyChanged(nameof(IsOnboardingPreview));
        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = false;
        IsPasswordSetupVisible = false;
        IsShellVisible = false;
        IsAccountListVisible = false;
        IsToolsVisible = false;
        IsSettingsVisible = false;
        IsSearchVisible = false;
        OnboardingSlideIndex = 0;
        IsOnboardingVisible = true;
        StatusText = _localization.GetString(AvaloniaStringKeys.OnboardingPreviewNotice);
        StatusSeverity = NotificationSeverity.Information;
    }

    private void ApplyLockedUiState()
    {
        CaptureResumeState();
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
                await EnterAuthorizedShellAsync();
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

            if (result == AuthorizationResult.Cancelled)
            {
                return;
            }

            QuickUnlockMessage = _localization.GetString(AvaloniaStringKeys.QuickUnlockFailed);
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

    public Task ReturnToQuickUnlockAsync()
    {
        if (!CanReturnToQuickUnlock()) return Task.CompletedTask;
        PasswordUnlock.Clear();
        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = true;
        QuickUnlockMessage = string.Empty;
        StatusText = _localization.GetString(AvaloniaStringKeys.VaultLockedQuickUnlock);
        StatusSeverity = NotificationSeverity.Information;
        return Task.CompletedTask;
    }

    public Task HandleWindowMinimizedAsync()
    {
        AccountList.SuspendRowCodeGeneration();
        if (!IsShellVisible || _settingsService?.Current.LockOnMinimize != true)
            return Task.CompletedTask;
        return LockAsync();
    }

    public void HandleWindowDeactivated() =>
        AccountList.SuspendRowCodeGeneration();

    public void HandleWindowActivated()
    {
        if (IsShellVisible && IsAccountListVisible && !IsSettingsVisible)
            AccountList.ResumeRowCodeGeneration();
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
        SettingsNotification.Clear();
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
        SettingsNotification.Clear();
        IsSettingsVisible = false;
    }

    private void SettingsNotificationShown(object? sender, NotificationShownEventArgs args) =>
        SettingsNotification.ShowTransient(args.Text, args.Severity);

    private void ClearSettingsNotifications()
    {
        foreach (var source in _settingsNotificationSources.Distinct())
            source.Clear();
        SettingsNotification.Clear();
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

    private async Task EnterAuthorizedShellAsync()
    {
        var resumeState = _resumeState;
        _resumeState = null;
        var activePage = resumeState?.ActivePage ?? ShellPage.Accounts;
        SetActivePage(activePage);

        await AccountList.LoadAsync();
        if (activePage == ShellPage.Accounts && resumeState?.AccountListState is not null)
            await AccountList.RestoreResumeStateAsync(resumeState.AccountListState);

        IsPasswordUnlockVisible = false;
        IsQuickUnlockVisible = false;
        IsPasswordSetupVisible = false;
        IsShellVisible = true;

        if (resumeState?.SettingsVisible == true)
        {
            try
            {
                await ShowSettingsAsync();
            }
            catch (Exception)
            {
                IsSettingsVisible = false;
            }
        }
    }

    private void CaptureResumeState()
    {
        if (!IsShellVisible) return;

        var activePage = IsToolsVisible ? ShellPage.Tools : ShellPage.Accounts;
        var accountListState = AccountList.CaptureResumeState();
        if (IsSettingsVisible)
        {
            accountListState = accountListState with
            {
                Target = AccountListResumeTarget.None,
                EntityId = null
            };
        }

        _resumeState = new ShellResumeState(
            activePage,
            IsSettingsVisible,
            accountListState);
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

    private bool CanReturnToQuickUnlock() =>
        _isPasswordUnlockVisible
        && !_isQuickUnlockBusy
        && _authorizationService.State is { IsUnlocked: false } state
        && state.PreferredUnlockMethod == PreferredUnlockMethod.PlatformQuickUnlock;

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
        _returnToQuickUnlockCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyOnboardingCommands()
    {
        _previousOnboardingCommand?.NotifyCanExecuteChanged();
        _nextOnboardingCommand?.NotifyCanExecuteChanged();
        _finishOnboardingCommand?.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed record ShellResumeState(
        ShellPage ActivePage,
        bool SettingsVisible,
        AccountListResumeState AccountListState);

    private sealed record OnboardingPreviewResumeState(
        bool PasswordUnlockVisible,
        bool QuickUnlockVisible,
        bool PasswordSetupVisible,
        bool ShellVisible,
        bool AccountListVisible,
        bool ToolsVisible,
        bool SettingsVisible,
        bool SearchVisible,
        string StatusText,
        NotificationSeverity StatusSeverity);
}
