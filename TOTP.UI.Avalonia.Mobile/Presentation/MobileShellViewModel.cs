using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed class MobileShellViewModel :
    INotifyPropertyChanged,
    IMobileLifecycleSink,
    IDisposable
{
    private static readonly TimeSpan BackgroundLockGracePeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CopiedNotificationDuration = TimeSpan.FromSeconds(1);

    private readonly IAuthorizationService _authorization;
    private readonly IPasswordValidationService _passwordValidation;
    private readonly IAccountManager _accountManager;
    private readonly IAccountTotpService _accountTotp;
    private readonly IAsyncClipboardService _clipboard;
    private readonly IMobileQrScanner _qrScanner;
    private readonly IQrPayloadValidator _qrPayloadValidator;
    private readonly IQrAccountImportService _qrImport;
    private readonly IAccountQrCodeService _accountQrCode;
    private readonly IMobileQrImageFactory _qrImageFactory;
    private readonly IMobileDocumentService _documents;
    private readonly IExportService _exportService;
    private readonly IAccountImportService _accountImport;
    private readonly ISettingsService _settings;
    private readonly IPlatformApplicationPaths _paths;
    private readonly MobileStringCatalog _strings;
    private readonly TimeProvider _timeProvider;
    private readonly MobileAsyncCommand _initializeCommand;
    private readonly MobileAsyncCommand _configureCommand;
    private readonly MobileAsyncCommand _unlockCommand;
    private readonly MobileAsyncCommand _biometricUnlockCommand;
    private readonly MobileAsyncCommand _beginBiometricEnrollmentCommand;
    private readonly MobileAsyncCommand _enableBiometricCommand;
    private readonly MobileAsyncCommand _cancelBiometricEnrollmentCommand;
    private readonly MobileAsyncCommand _beginDisableAppLockCommand;
    private readonly MobileAsyncCommand _confirmDisableAppLockCommand;
    private readonly MobileAsyncCommand _cancelDisableAppLockCommand;
    private readonly MobileAsyncCommand _enableAppLockCommand;
    private readonly MobileAsyncCommand _lockCommand;
    private readonly MobileAsyncCommand _showAccountsCommand;
    private readonly MobileAsyncCommand _showSettingsCommand;
    private readonly MobileAsyncCommand _beginAddCommand;
    private readonly MobileAsyncCommand _saveAccountCommand;
    private readonly MobileAsyncCommand _cancelEditCommand;
    private readonly MobileAsyncCommand _confirmDeleteCommand;
    private readonly MobileAsyncCommand _cancelDeleteCommand;
    private readonly MobileAsyncCommand _scanQrCommand;
    private readonly MobileAsyncCommand _importGoogleQrCommand;
    private readonly MobileAsyncCommand _updateQrConflictCommand;
    private readonly MobileAsyncCommand _keepBothQrConflictCommand;
    private readonly MobileAsyncCommand _cancelQrConflictCommand;
    private readonly MobileAsyncCommand _dismissQrCommand;
    private readonly MobileAsyncCommand _exportBackupCommand;
    private readonly MobileAsyncCommand _importBackupCommand;
    private readonly MobileAsyncCommand _confirmImportCommand;
    private readonly MobileAsyncCommand _cancelImportCommand;
    private readonly MobileAsyncCommand _selectEnglishLanguageCommand;
    private readonly MobileAsyncCommand _selectGermanLanguageCommand;
    private readonly MobileAsyncCommand _selectFrenchLanguageCommand;
    private readonly MobileAsyncCommand _selectSpanishLanguageCommand;

    private MobileScreen _screen = MobileScreen.Starting;
    private bool _isBusy;
    private bool _startupFailed;
    private string _notificationText = string.Empty;
    private NotificationSeverity _notificationSeverity = NotificationSeverity.Information;
    private string _setupPassword = string.Empty;
    private string _setupConfirmation = string.Empty;
    private string _unlockPassword = string.Empty;
    private bool _isBiometricAvailable;
    private bool _isBiometricEnabled;
    private bool _isBiometricEnrollmentVisible;
    private string _biometricRecoveryPassword = string.Empty;
    private string _appLockRecoveryPassword = string.Empty;
    private bool _isDisableAppLockConfirmationVisible;
    private bool _isSettingsVisible;
    private string _searchText = string.Empty;
    private readonly List<MobileAccountItem> _allAccounts = [];
    private MobileAccountItem? _selectedAccount;
    private bool _isEditorVisible;
    private bool _isDeleteConfirmationVisible;
    private Guid? _pendingDeleteAccountId;
    private string _pendingDeleteDisplayName = string.Empty;
    private Guid? _editingAccountId;
    private string _editorIssuer = string.Empty;
    private string _editorAccountName = string.Empty;
    private string _editorSecret = string.Empty;
    private int _editorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
    private bool _isAdvancedOptionsExpanded;
    private bool _isQrConflictVisible;
    private string _qrConflictDisplayName = string.Empty;
    private TaskCompletionSource<QrAccountConflictDecision>? _qrConflictCompletion;
    private MobileQrImageHandle? _qrImage;
    private string _backupPassword = string.Empty;
    private string _backupPasswordConfirmation = string.Empty;
    private string _importPassword = string.Empty;
    private bool _isImportConfirmationVisible;
    private string _importConfirmationText = string.Empty;
    private TaskCompletionSource<bool>? _importConfirmationCompletion;
    private CancellationTokenSource? _sensitiveOperationLifetime;
    private CancellationTokenSource? _codeLifetime;
    private CancellationTokenSource? _notificationLifetime;
    private long? _backgroundedAtTimestamp;
    private ITimer? _backgroundLockTimer;
    private bool _automaticBiometricUnlockPending;
    private bool _disposed;

    public MobileShellViewModel(
        IAuthorizationService authorization,
        IPasswordValidationService passwordValidation,
        IAccountManager accountManager,
        IAccountTotpService accountTotp,
        IAsyncClipboardService clipboard,
        IMobileQrScanner qrScanner,
        IQrPayloadValidator qrPayloadValidator,
        IQrAccountImportService qrImport,
        IAccountQrCodeService accountQrCode,
        IMobileQrImageFactory qrImageFactory,
        IMobileDocumentService documents,
        IExportService exportService,
        IAccountImportService accountImport,
        ISettingsService settings,
        IPlatformApplicationPaths paths,
        MobileStringCatalog strings,
        TimeProvider timeProvider)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _passwordValidation = passwordValidation
            ?? throw new ArgumentNullException(nameof(passwordValidation));
        _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        _accountTotp = accountTotp ?? throw new ArgumentNullException(nameof(accountTotp));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _qrScanner = qrScanner ?? throw new ArgumentNullException(nameof(qrScanner));
        _qrPayloadValidator = qrPayloadValidator
            ?? throw new ArgumentNullException(nameof(qrPayloadValidator));
        _qrImport = qrImport ?? throw new ArgumentNullException(nameof(qrImport));
        _accountQrCode = accountQrCode ?? throw new ArgumentNullException(nameof(accountQrCode));
        _qrImageFactory = qrImageFactory ?? throw new ArgumentNullException(nameof(qrImageFactory));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _accountImport = accountImport ?? throw new ArgumentNullException(nameof(accountImport));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _initializeCommand = new MobileAsyncCommand(InitializeAsync, () => !IsBusy);
        _configureCommand = new MobileAsyncCommand(ConfigureAsync, () => IsSetupVisible && !IsBusy);
        _unlockCommand = new MobileAsyncCommand(
            UnlockAsync,
            () => IsUnlockVisible && !IsBusy && UnlockPassword.Length > 0);
        _biometricUnlockCommand = new MobileAsyncCommand(
            BiometricUnlockAsync,
            () => IsBiometricUnlockVisible && !IsBusy);
        _beginBiometricEnrollmentCommand = new MobileAsyncCommand(
            BeginBiometricEnrollmentAsync,
            () => IsBiometricSetupAvailable && !IsBusy);
        _enableBiometricCommand = new MobileAsyncCommand(
            EnableBiometricAsync,
            () => IsBiometricEnrollmentVisible
                && !IsBusy
                && BiometricRecoveryPassword.Length > 0);
        _cancelBiometricEnrollmentCommand = new MobileAsyncCommand(
            CancelBiometricEnrollmentAsync,
            () => IsBiometricEnrollmentVisible && !IsBusy);
        _beginDisableAppLockCommand = new MobileAsyncCommand(
            BeginDisableAppLockAsync,
            () => IsSettingsVisible && IsAppLockEnabled && !IsBusy);
        _confirmDisableAppLockCommand = new MobileAsyncCommand(
            ConfirmDisableAppLockAsync,
            () => IsDisableAppLockConfirmationVisible
                && AppLockRecoveryPassword.Length > 0
                && !IsBusy);
        _cancelDisableAppLockCommand = new MobileAsyncCommand(
            CancelDisableAppLockAsync,
            () => IsDisableAppLockConfirmationVisible && !IsBusy);
        _enableAppLockCommand = new MobileAsyncCommand(
            EnableAppLockAsync,
            () => IsSettingsVisible && !IsAppLockEnabled && !IsBusy);
        _lockCommand = new MobileAsyncCommand(
            LockAsync,
            () => IsAccountsVisible && IsAppLockEnabled);
        _showAccountsCommand = new MobileAsyncCommand(
            ShowAccountsAsync,
            () => IsAccountsVisible && IsSettingsVisible && !IsBusy);
        _showSettingsCommand = new MobileAsyncCommand(
            ShowSettingsAsync,
            () => IsAccountsVisible && !IsSettingsVisible && !IsEditorVisible && !IsBusy);
        _beginAddCommand = new MobileAsyncCommand(BeginAddAsync, CanEditAccounts);
        _saveAccountCommand = new MobileAsyncCommand(
            SaveAccountAsync,
            () => IsEditorVisible && !IsBusy);
        _cancelEditCommand = new MobileAsyncCommand(
            CancelEditAsync,
            () => IsEditorVisible && !IsBusy);
        _confirmDeleteCommand = new MobileAsyncCommand(
            ConfirmDeleteAsync,
            () => IsDeleteConfirmationVisible && !IsBusy);
        _cancelDeleteCommand = new MobileAsyncCommand(
            CancelDeleteAsync,
            () => IsDeleteConfirmationVisible && !IsBusy);
        _scanQrCommand = new MobileAsyncCommand(ScanQrAsync, CanEditAccounts);
        _importGoogleQrCommand = new MobileAsyncCommand(
            ImportGoogleQrAsync,
            () => IsSettingsVisible && !IsBusy);
        _updateQrConflictCommand = new MobileAsyncCommand(
            () => ResolveQrConflictAsync(QrAccountConflictDecision.UpdateExisting),
            () => IsQrConflictVisible);
        _keepBothQrConflictCommand = new MobileAsyncCommand(
            () => ResolveQrConflictAsync(QrAccountConflictDecision.KeepBoth),
            () => IsQrConflictVisible);
        _cancelQrConflictCommand = new MobileAsyncCommand(
            () => ResolveQrConflictAsync(QrAccountConflictDecision.Cancel),
            () => IsQrConflictVisible);
        _dismissQrCommand = new MobileAsyncCommand(
            DismissQrAsync,
            () => HasQrImage);
        _exportBackupCommand = new MobileAsyncCommand(
            ExportBackupAsync,
            () => IsSettingsVisible && !IsBusy);
        _importBackupCommand = new MobileAsyncCommand(
            ImportBackupAsync,
            () => IsSettingsVisible && !IsBusy);
        _confirmImportCommand = new MobileAsyncCommand(
            () => ResolveImportConfirmationAsync(true),
            () => IsImportConfirmationVisible);
        _cancelImportCommand = new MobileAsyncCommand(
            () => ResolveImportConfirmationAsync(false),
            () => IsImportConfirmationVisible);
        _selectEnglishLanguageCommand = new MobileAsyncCommand(
            () => SelectLanguageAsync("en"),
            () => IsSettingsVisible && !IsBusy);
        _selectGermanLanguageCommand = new MobileAsyncCommand(
            () => SelectLanguageAsync("de"),
            () => IsSettingsVisible && !IsBusy);
        _selectFrenchLanguageCommand = new MobileAsyncCommand(
            () => SelectLanguageAsync("fr"),
            () => IsSettingsVisible && !IsBusy);
        _selectSpanishLanguageCommand = new MobileAsyncCommand(
            () => SelectLanguageAsync("es"),
            () => IsSettingsVisible && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MobileAccountItem> Accounts { get; } = [];

    public ICommand InitializeCommand => _initializeCommand;
    public ICommand ConfigureCommand => _configureCommand;
    public ICommand UnlockCommand => _unlockCommand;
    public ICommand BiometricUnlockCommand => _biometricUnlockCommand;
    public ICommand BeginBiometricEnrollmentCommand => _beginBiometricEnrollmentCommand;
    public ICommand EnableBiometricCommand => _enableBiometricCommand;
    public ICommand CancelBiometricEnrollmentCommand => _cancelBiometricEnrollmentCommand;
    public ICommand BeginDisableAppLockCommand => _beginDisableAppLockCommand;
    public ICommand ConfirmDisableAppLockCommand => _confirmDisableAppLockCommand;
    public ICommand CancelDisableAppLockCommand => _cancelDisableAppLockCommand;
    public ICommand EnableAppLockCommand => _enableAppLockCommand;
    public ICommand LockCommand => _lockCommand;
    public ICommand ShowAccountsCommand => _showAccountsCommand;
    public ICommand ShowSettingsCommand => _showSettingsCommand;
    public ICommand BeginAddCommand => _beginAddCommand;
    public ICommand SaveAccountCommand => _saveAccountCommand;
    public ICommand CancelEditCommand => _cancelEditCommand;
    public ICommand ConfirmDeleteCommand => _confirmDeleteCommand;
    public ICommand CancelDeleteCommand => _cancelDeleteCommand;
    public ICommand ScanQrCommand => _scanQrCommand;
    public ICommand ImportGoogleQrCommand => _importGoogleQrCommand;
    public ICommand UpdateQrConflictCommand => _updateQrConflictCommand;
    public ICommand KeepBothQrConflictCommand => _keepBothQrConflictCommand;
    public ICommand CancelQrConflictCommand => _cancelQrConflictCommand;
    public ICommand DismissQrCommand => _dismissQrCommand;
    public ICommand ExportBackupCommand => _exportBackupCommand;
    public ICommand ImportBackupCommand => _importBackupCommand;
    public ICommand ConfirmImportCommand => _confirmImportCommand;
    public ICommand CancelImportCommand => _cancelImportCommand;
    public ICommand SelectEnglishLanguageCommand => _selectEnglishLanguageCommand;
    public ICommand SelectGermanLanguageCommand => _selectGermanLanguageCommand;
    public ICommand SelectFrenchLanguageCommand => _selectFrenchLanguageCommand;
    public ICommand SelectSpanishLanguageCommand => _selectSpanishLanguageCommand;

    public bool IsStartingVisible => _screen == MobileScreen.Starting;
    public bool IsSetupVisible => _screen == MobileScreen.Setup;
    public bool IsUnlockVisible => _screen == MobileScreen.Unlock;
    public bool IsAccountsVisible => _screen == MobileScreen.Accounts;
    public bool IsAccountListVisible => IsAccountsVisible && !IsSettingsVisible && !IsEditorVisible;
    public bool IsSettingsVisible => IsAccountsVisible && _isSettingsVisible;
    public bool HasAccounts => Accounts.Count > 0;
    public bool HasNoAccounts => _allAccounts.Count == 0;
    public bool HasNoSearchResults => _allAccounts.Count > 0 && Accounts.Count == 0;
    public IImage? QrImage => _qrImage?.Image;
    public bool HasQrImage => QrImage is not null;
    public bool CanRetry => _startupFailed && !IsBusy;
    public bool IsBiometricUnlockVisible =>
        IsUnlockVisible && IsBiometricEnabled && IsBiometricAvailable;
    public bool IsBiometricSetupAvailable =>
        IsSettingsVisible && IsAppLockEnabled && IsBiometricAvailable && !IsBiometricEnabled;
    public bool IsBiometricUnavailable => IsSettingsVisible && !IsBiometricAvailable;
    public bool IsQrConflictVisible
    {
        get => _isQrConflictVisible;
        private set
        {
            if (!SetField(ref _isQrConflictVisible, value)) return;
            NotifyCommands();
        }
    }
    public bool IsImportConfirmationVisible
    {
        get => _isImportConfirmationVisible;
        private set
        {
            if (!SetField(ref _isImportConfirmationVisible, value)) return;
            NotifyCommands();
        }
    }
    public bool IsBiometricEnrollmentStartVisible =>
        IsBiometricSetupAvailable && !IsBiometricEnrollmentVisible;
    public bool IsAppLockEnabled => _settings.Current.AppLockEnabled;
    public bool IsAppLockDisabled => !IsAppLockEnabled;
    public bool IsManualLockVisible => IsAccountsVisible && IsAppLockEnabled;

    public bool IsDisableAppLockConfirmationVisible
    {
        get => _isDisableAppLockConfirmationVisible;
        private set
        {
            if (!SetField(ref _isDisableAppLockConfirmationVisible, value)) return;
            NotifyCommands();
        }
    }
    public bool IsEnglishLanguageSelected => _strings.Culture.TwoLetterISOLanguageName == "en";
    public bool IsGermanLanguageSelected => _strings.Culture.TwoLetterISOLanguageName == "de";
    public bool IsFrenchLanguageSelected => _strings.Culture.TwoLetterISOLanguageName == "fr";
    public bool IsSpanishLanguageSelected => _strings.Culture.TwoLetterISOLanguageName == "es";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanRetry));
            NotifyCommands();
        }
    }

    public string NotificationText
    {
        get => _notificationText;
        private set => SetField(ref _notificationText, value);
    }

    public NotificationSeverity NotificationSeverity
    {
        get => _notificationSeverity;
        private set => SetField(ref _notificationSeverity, value);
    }

    public string SetupPassword
    {
        get => _setupPassword;
        set
        {
            if (!SetField(ref _setupPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string SetupConfirmation
    {
        get => _setupConfirmation;
        set
        {
            if (!SetField(ref _setupConfirmation, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string UnlockPassword
    {
        get => _unlockPassword;
        set
        {
            if (!SetField(ref _unlockPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
            _unlockCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBiometricAvailable
    {
        get => _isBiometricAvailable;
        private set
        {
            if (!SetField(ref _isBiometricAvailable, value)) return;
            OnPropertyChanged(nameof(IsBiometricSetupAvailable));
            OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
            OnPropertyChanged(nameof(IsBiometricUnavailable));
            NotifyCommands();
        }
    }

    public bool IsBiometricEnabled
    {
        get => _isBiometricEnabled;
        private set
        {
            if (!SetField(ref _isBiometricEnabled, value)) return;
            OnPropertyChanged(nameof(IsBiometricUnlockVisible));
            OnPropertyChanged(nameof(IsBiometricSetupAvailable));
            OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
            NotifyCommands();
        }
    }

    public bool IsBiometricEnrollmentVisible
    {
        get => _isBiometricEnrollmentVisible;
        private set
        {
            if (!SetField(ref _isBiometricEnrollmentVisible, value)) return;
            OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
            NotifyCommands();
        }
    }

    public string BiometricRecoveryPassword
    {
        get => _biometricRecoveryPassword;
        set
        {
            if (!SetField(ref _biometricRecoveryPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
            _enableBiometricCommand.NotifyCanExecuteChanged();
        }
    }

    public string AppLockRecoveryPassword
    {
        get => _appLockRecoveryPassword;
        set
        {
            if (!SetField(ref _appLockRecoveryPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
            _confirmDisableAppLockCommand.NotifyCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty)) return;
            ApplyAccountFilter();
        }
    }

    public string BackupPassword
    {
        get => _backupPassword;
        set
        {
            if (!SetField(ref _backupPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string BackupPasswordConfirmation
    {
        get => _backupPasswordConfirmation;
        set
        {
            if (!SetField(ref _backupPasswordConfirmation, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string ImportPassword
    {
        get => _importPassword;
        set
        {
            if (!SetField(ref _importPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public MobileAccountItem? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (IsDeleteConfirmationVisible && value?.Id != _pendingDeleteAccountId) return;
            if (!SetField(ref _selectedAccount, value)) return;
            ClearQrImage();
            NotifyCommands();
        }
    }

    public bool IsEditorVisible
    {
        get => _isEditorVisible;
        private set
        {
            if (!SetField(ref _isEditorVisible, value)) return;
            OnPropertyChanged(nameof(IsAccountListVisible));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(EditorSecretPlaceholder));
            NotifyCommands();
        }
    }

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set
        {
            if (!SetField(ref _isDeleteConfirmationVisible, value)) return;
            if (!value)
            {
                _pendingDeleteAccountId = null;
                _pendingDeleteDisplayName = string.Empty;
                OnPropertyChanged(nameof(DeletePrompt));
            }
            NotifyCommands();
        }
    }

    public string EditorIssuer
    {
        get => _editorIssuer;
        set
        {
            if (!SetField(ref _editorIssuer, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string EditorAccountName
    {
        get => _editorAccountName;
        set
        {
            if (!SetField(ref _editorAccountName, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string EditorSecret
    {
        get => _editorSecret;
        set
        {
            if (!SetField(ref _editorSecret, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public int EditorPeriodSeconds
    {
        get => _editorPeriodSeconds;
        set
        {
            if (!SetField(ref _editorPeriodSeconds, value)) return;
            ClearErrorNotification();
        }
    }

    public bool IsAdvancedOptionsExpanded
    {
        get => _isAdvancedOptionsExpanded;
        set => SetField(ref _isAdvancedOptionsExpanded, value);
    }

    public string EditorTitle => Get(_editingAccountId.HasValue
        ? MobileStringKeys.EditorEditTitle
        : MobileStringKeys.EditorAddTitle);

    public string EditorSecretPlaceholder => Get(_editingAccountId.HasValue
        ? MobileStringKeys.SecretOptionalOnEdit
        : MobileStringKeys.Secret);

    public string DeletePrompt => string.Format(
        Get(MobileStringKeys.DeleteAccountPrompt),
        _pendingDeleteDisplayName);

    public string StartingText => Get(MobileStringKeys.Starting);
    public string AppTitle => Get(MobileStringKeys.AppTitle);
    public string RetryText => Get(MobileStringKeys.Retry);
    public string SetupTitle => Get(MobileStringKeys.SetupTitle);
    public string SetupDescription => Get(MobileStringKeys.SetupDescription);
    public string MasterPasswordText => Get(MobileStringKeys.MasterPassword);
    public string ConfirmPasswordText => Get(MobileStringKeys.ConfirmPassword);
    public string CreateVaultText => Get(MobileStringKeys.CreateVault);
    public string UnlockTitle => Get(MobileStringKeys.UnlockTitle);
    public string UnlockDescription => Get(MobileStringKeys.UnlockDescription);
    public string UnlockText => Get(MobileStringKeys.Unlock);
    public string AccountsTitle => Get(MobileStringKeys.AccountsTitle);
    public string NoAccountsText => Get(MobileStringKeys.NoAccounts);
    public string AddAccountText => Get(MobileStringKeys.AddAccount);
    public string EditAccountText => Get(MobileStringKeys.EditAccount);
    public string DeleteAccountText => Get(MobileStringKeys.DeleteAccount);
    public string LockText => Get(MobileStringKeys.Lock);
    public string IssuerText => Get(MobileStringKeys.Issuer);
    public string AccountNameText => Get(MobileStringKeys.AccountName);
    public string AdvancedOptionsText => Get(MobileStringKeys.AdvancedOptions);
    public string TotpPeriodText => Get(MobileStringKeys.TotpPeriod);
    public string TotpPeriodHelpText => Get(MobileStringKeys.TotpPeriodHelp);
    public string SaveText => Get(MobileStringKeys.Save);
    public string CancelText => Get(MobileStringKeys.Cancel);
    public string CopyCodeText => Get(MobileStringKeys.CopyCode);
    public string DeleteConfirmTitle => Get(MobileStringKeys.DeleteConfirmTitle);
    public string DeleteText => Get(MobileStringKeys.Delete);
    public string BiometricUnlockText => Get(MobileStringKeys.BiometricUnlock);
    public string BiometricSetupTitle => Get(MobileStringKeys.BiometricSetupTitle);
    public string BiometricSetupDescription => Get(MobileStringKeys.BiometricSetupDescription);
    public string BiometricEnableText => Get(MobileStringKeys.BiometricEnable);
    public string BiometricEnabledText => Get(MobileStringKeys.BiometricEnabled);
    public string BiometricUnavailableText => Get(MobileStringKeys.BiometricUnavailable);
    public string CodesText => Get(MobileStringKeys.Codes);
    public string SettingsText => Get(MobileStringKeys.Settings);
    public string LanguageText => Get(MobileStringKeys.Language);
    public string EnglishLanguageText => Get(MobileStringKeys.EnglishLanguage);
    public string GermanLanguageText => Get(MobileStringKeys.GermanLanguage);
    public string FrenchLanguageText => Get(MobileStringKeys.FrenchLanguage);
    public string SpanishLanguageText => Get(MobileStringKeys.SpanishLanguage);
    public string SecurityText => Get(MobileStringKeys.Security);
    public string AppLockTitleText => Get(MobileStringKeys.AppLockTitle);
    public string AppLockEnabledDescriptionText =>
        Get(MobileStringKeys.AppLockEnabledDescription);
    public string AppLockDisabledDescriptionText =>
        Get(MobileStringKeys.AppLockDisabledDescription);
    public string DisableAppLockText => Get(MobileStringKeys.DisableAppLock);
    public string EnableAppLockText => Get(MobileStringKeys.EnableAppLock);
    public string DisableAppLockWarningText => Get(MobileStringKeys.DisableAppLockWarning);
    public string SearchAccountsText => Get(MobileStringKeys.SearchAccounts);
    public string NoSearchResultsText => Get(MobileStringKeys.NoSearchResults);
    public string AccountSwipeHintText => Get(MobileStringKeys.AccountSwipeHint);
    public string ScanQrText => Get(MobileStringKeys.ScanQr);
    public string ImportExportText => Get(MobileStringKeys.ImportExport);
    public string ImportGoogleQrText => Get(MobileStringKeys.ImportGoogleQr);
    public string ImportGoogleQrDescriptionText =>
        Get(MobileStringKeys.ImportGoogleQrDescription);
    public string QrConflictTitle => Get(MobileStringKeys.QrConflictTitle);
    public string QrConflictPrompt => string.Format(
        Get(MobileStringKeys.QrConflictPrompt),
        _qrConflictDisplayName);
    public string UpdateExistingText => Get(MobileStringKeys.UpdateExisting);
    public string KeepBothText => Get(MobileStringKeys.KeepBoth);
    public string ShowQrText => Get(MobileStringKeys.ShowQr);
    public string DismissQrText => Get(MobileStringKeys.DismissQr);
    public string QrPrivacyNoticeText => Get(MobileStringKeys.QrPrivacyNotice);
    public string BackupTitle => Get(MobileStringKeys.BackupTitle);
    public string BackupDescription => Get(MobileStringKeys.BackupDescription);
    public string BackupPasswordText => Get(MobileStringKeys.BackupPassword);
    public string ConfirmBackupPasswordText => Get(MobileStringKeys.ConfirmBackupPassword);
    public string ExportBackupText => Get(MobileStringKeys.ExportBackup);
    public string ImportBackupText => Get(MobileStringKeys.ImportBackup);
    public string ImportConfirmationTitle => Get(MobileStringKeys.ImportConfirmationTitle);
    public string ImportConfirmationText
    {
        get => _importConfirmationText;
        private set => SetField(ref _importConfirmationText, value);
    }
    public string ConfirmImportText => Get(MobileStringKeys.ConfirmImport);

    public async Task InitializeAsync()
    {
        if (IsBusy || _disposed) return;

        IsBusy = true;
        _startupFailed = false;
        SetScreen(MobileScreen.Starting);
        SetNotification(Get(MobileStringKeys.Starting), NotificationSeverity.Information);
        try
        {
            var settingsResult = await _settings.LoadAsync();
            if (settingsResult.IsFailed)
            {
                FailStartup();
                return;
            }

            _strings.ApplyCulture(_settings.Current.CultureName);
            NotifyLocalizedTextChanged();
            SetNotification(Get(MobileStringKeys.Starting), NotificationSeverity.Information);

            await _authorization.InitializeAsync();
            IsBiometricAvailable = await _authorization.IsHelloAvailableAsync();
            IsBiometricEnabled = _settings.Current.AppLockEnabled
                && _authorization.State.ConfiguredGate == AuthorizationGateKind.Hello;
            if (!_authorization.State.IsConfigured
                && File.Exists(_paths.AuthorizationEnvelopeFilePath))
            {
                FailStartup();
                return;
            }

            ClearNotification();
            if (_authorization.State.IsConfigured && !_settings.Current.AppLockEnabled)
            {
                var automaticUnlock = await _authorization.TryUnlockOnStartupAsync();
                if (automaticUnlock == AuthorizationResult.Success)
                {
                    SetScreen(MobileScreen.Accounts);
                    await LoadAccountsAsync();
                    return;
                }
            }

            SetScreen(_authorization.State.IsConfigured
                ? MobileScreen.Unlock
                : MobileScreen.Setup);
        }
        catch (Exception)
        {
            FailStartup();
        }
        finally
        {
            IsBusy = false;
            TryStartAutomaticBiometricUnlock();
        }
    }

    public async Task ConfigureAsync()
    {
        if (!IsSetupVisible || IsBusy) return;

        var password = SetupPassword;
        var confirmation = SetupConfirmation;
        SetupPassword = string.Empty;
        SetupConfirmation = string.Empty;

        if (string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(confirmation))
        {
            SetError(MobileStringKeys.PasswordRequired);
            return;
        }

        if (password.Length < _passwordValidation.MinimumLength)
        {
            SetNotification(
                string.Format(
                    Get(MobileStringKeys.PasswordMinimumLength),
                    _passwordValidation.MinimumLength),
                NotificationSeverity.Error);
            return;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            SetError(MobileStringKeys.PasswordMismatch);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authorization.ConfigurePasswordAsync(password, confirmation);
            if (result != AuthorizationResult.Success)
            {
                SetError(result == AuthorizationResult.ExistingVaultConflict
                    ? MobileStringKeys.ExistingVaultConflict
                    : MobileStringKeys.SetupFailed);
                return;
            }

            SetScreen(MobileScreen.Accounts);
            await LoadAccountsAsync();
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.SetupFailed);
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
            IsBusy = false;
        }
    }

    public async Task UnlockAsync()
    {
        if (!IsUnlockVisible || IsBusy || UnlockPassword.Length == 0) return;

        var password = UnlockPassword;
        UnlockPassword = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authorization.TryUnlockWithPasswordAsync(password);
            if (result != AuthorizationResult.Success)
            {
                SetError(result == AuthorizationResult.InvalidCredentials
                    ? MobileStringKeys.UnlockRejected
                    : MobileStringKeys.UnlockFailed);
                return;
            }

            SetScreen(MobileScreen.Accounts);
            await LoadAccountsAsync();
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.UnlockFailed);
        }
        finally
        {
            password = string.Empty;
            IsBusy = false;
        }
    }

    public async Task BiometricUnlockAsync()
    {
        if (!IsBiometricUnlockVisible || IsBusy) return;

        IsBusy = true;
        ClearNotification();
        try
        {
            var result = await _authorization.TryUnlockWithHelloAsync();
            if (result == AuthorizationResult.Success)
            {
                ClearNotification();
                SetScreen(MobileScreen.Accounts);
                await LoadAccountsAsync();
                return;
            }

            if (result == AuthorizationResult.Cancelled) return;
            SetError(result switch
            {
                AuthorizationResult.PasswordRequired =>
                    MobileStringKeys.BiometricRecoveryRequired,
                AuthorizationResult.TooManyAttempts =>
                    MobileStringKeys.BiometricRetriesExhausted,
                AuthorizationResult.DisabledByPolicy =>
                    MobileStringKeys.BiometricDisabledByPolicy,
                _ => MobileStringKeys.BiometricUnlockFailed
            });
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.BiometricUnlockFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task BeginBiometricEnrollmentAsync()
    {
        if (!IsBiometricSetupAvailable || IsBusy) return Task.CompletedTask;
        BiometricRecoveryPassword = string.Empty;
        IsBiometricEnrollmentVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public async Task EnableBiometricAsync()
    {
        if (!IsBiometricEnrollmentVisible
            || IsBusy
            || BiometricRecoveryPassword.Length == 0)
        {
            return;
        }

        var recoveryPassword = BiometricRecoveryPassword;
        BiometricRecoveryPassword = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authorization.ConfigureHelloAsync(recoveryPassword);
            if (result == AuthorizationResult.Cancelled) return;
            if (result != AuthorizationResult.Success)
            {
                SetError(result switch
                {
                    AuthorizationResult.InvalidCredentials => MobileStringKeys.UnlockRejected,
                    AuthorizationResult.DisabledByPolicy =>
                        MobileStringKeys.BiometricDisabledByPolicy,
                    AuthorizationResult.TooManyAttempts =>
                        MobileStringKeys.BiometricRetriesExhausted,
                    _ => MobileStringKeys.BiometricEnableFailed
                });
                return;
            }

            IsBiometricEnabled = true;
            IsBiometricEnrollmentVisible = false;
            BiometricRecoveryPassword = string.Empty;
            SetSuccess(MobileStringKeys.BiometricEnabled);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.BiometricEnableFailed);
        }
        finally
        {
            recoveryPassword = string.Empty;
            IsBusy = false;
        }
    }

    public Task CancelBiometricEnrollmentAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        BiometricRecoveryPassword = string.Empty;
        IsBiometricEnrollmentVisible = false;
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task BeginDisableAppLockAsync()
    {
        if (!IsSettingsVisible || !IsAppLockEnabled || IsBusy)
            return Task.CompletedTask;
        AppLockRecoveryPassword = string.Empty;
        IsDisableAppLockConfirmationVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public async Task ConfirmDisableAppLockAsync()
    {
        if (!IsDisableAppLockConfirmationVisible
            || IsBusy
            || AppLockRecoveryPassword.Length == 0)
        {
            return;
        }

        var recoveryPassword = AppLockRecoveryPassword;
        AppLockRecoveryPassword = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authorization.SetAppLockEnabledAsync(
                false,
                recoveryPassword);
            if (result != AuthorizationResult.Success)
            {
                SetError(result == AuthorizationResult.InvalidCredentials
                    ? MobileStringKeys.UnlockRejected
                    : MobileStringKeys.AppLockChangeFailed);
                return;
            }

            IsDisableAppLockConfirmationVisible = false;
            IsBiometricEnabled = false;
            NotifyAppLockChanged();
            SetNotification(
                Get(MobileStringKeys.AppLockDisabled),
                NotificationSeverity.Warning);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.AppLockChangeFailed);
        }
        finally
        {
            recoveryPassword = string.Empty;
            IsBusy = false;
        }
    }

    public Task CancelDisableAppLockAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        AppLockRecoveryPassword = string.Empty;
        IsDisableAppLockConfirmationVisible = false;
        ClearNotification();
        return Task.CompletedTask;
    }

    public async Task EnableAppLockAsync()
    {
        if (!IsSettingsVisible || IsAppLockEnabled || IsBusy) return;

        IsBusy = true;
        try
        {
            var result = await _authorization.SetAppLockEnabledAsync(true, string.Empty);
            if (result != AuthorizationResult.Success)
            {
                SetError(MobileStringKeys.AppLockChangeFailed);
                return;
            }

            NotifyAppLockChanged();
            SetSuccess(MobileStringKeys.AppLockEnabled);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.AppLockChangeFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task LockAsync()
    {
        if (!IsAppLockEnabled) return Task.CompletedTask;
        LockCore();
        return Task.CompletedTask;
    }

    public Task ShowAccountsAsync()
    {
        if (!IsSettingsVisible || IsBusy) return Task.CompletedTask;

        _isSettingsVisible = false;
        IsBiometricEnrollmentVisible = false;
        IsDisableAppLockConfirmationVisible = false;
        ClearPasswordInputs();
        ClearNotification();
        NotifyUnlockedSectionChanged();
        StartCodeRefresh();
        return Task.CompletedTask;
    }

    public Task ShowSettingsAsync()
    {
        if (!IsAccountsVisible || IsSettingsVisible || IsEditorVisible || IsBusy)
            return Task.CompletedTask;

        _isSettingsVisible = true;
        IsDeleteConfirmationVisible = false;
        IsDisableAppLockConfirmationVisible = false;
        CancelCodeRefresh();
        ClearQrImage();
        ClearNotification();
        NotifyUnlockedSectionChanged();
        return Task.CompletedTask;
    }

    public async Task SelectLanguageAsync(string cultureName)
    {
        if (!IsSettingsVisible || IsBusy) return;

        var selectedCulture = cultureName.ToLowerInvariant() switch
        {
            "de" => "de",
            "fr" => "fr",
            "es" => "es",
            _ => "en"
        };
        if (_strings.Culture.TwoLetterISOLanguageName.Equals(
                selectedCulture,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousCulture = _settings.Current.CultureName;
        IsBusy = true;
        try
        {
            _settings.Current.CultureName = selectedCulture;
            var saved = await _settings.SaveAsync();
            if (saved.IsFailed)
            {
                _settings.Current.CultureName = previousCulture;
                SetError(MobileStringKeys.LanguageSaveFailed);
                return;
            }

            _strings.ApplyCulture(selectedCulture);
            NotifyLocalizedTextChanged();
            ClearNotification();
        }
        catch (Exception)
        {
            _settings.Current.CultureName = previousCulture;
            SetError(MobileStringKeys.LanguageSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ScanQrAsync() => ScanQrCoreAsync(fromSettings: false);

    public Task ImportGoogleQrAsync() => ScanQrCoreAsync(fromSettings: true);

    private async Task ScanQrCoreAsync(bool fromSettings)
    {
        if (fromSettings
            ? !IsSettingsVisible || IsBusy
            : !CanEditAccounts())
        {
            return;
        }

        IsBusy = true;
        ClearNotification();
        string? payload = null;
        using var operation = BeginSensitiveOperation();
        try
        {
            var scanned = await _qrScanner.ScanAsync(operation.Token);
            if (scanned.Status == MobileQrScanStatus.Cancelled) return;
            if (scanned.Status == MobileQrScanStatus.Unavailable)
            {
                SetError(MobileStringKeys.QrScannerUnavailable);
                return;
            }

            if (scanned.Status != MobileQrScanStatus.Success
                || string.IsNullOrWhiteSpace(scanned.Payload))
            {
                SetError(MobileStringKeys.QrScanFailed);
                return;
            }

            if (!_authorization.State.IsUnlocked
                || fromSettings && !IsSettingsVisible
                || !fromSettings && !IsAccountListVisible)
            {
                SetError(MobileStringKeys.QrScanRetryAfterUnlock);
                return;
            }

            payload = scanned.Payload;
            var validation = _qrPayloadValidator.Validate(payload);
            if (!validation.IsValid)
            {
                SetError(MobileStringKeys.QrInvalid);
                return;
            }

            if (validation.Kind == QrPayloadKind.GoogleAuthenticatorMigration
                && !await ConfirmQrMigrationAsync(validation.AccountCount, operation.Token))
            {
                SetNotification(
                    Get(MobileStringKeys.QrImportCancelled),
                    NotificationSeverity.Information);
                return;
            }

            var imported = await _qrImport.ImportAsync(
                payload,
                ResolveQrConflictAsync,
                operation.Token);
            if (imported.IsFailed)
            {
                SetError(MobileStringKeys.QrInvalid);
                return;
            }

            var outcome = imported.Value;
            switch (outcome.Status)
            {
                case QrAccountImportStatus.Added:
                    await LoadAccountsAsync(outcome.AccountId);
                    SetSuccess(MobileStringKeys.QrAccountAdded);
                    break;
                case QrAccountImportStatus.Updated:
                    await LoadAccountsAsync(outcome.AccountId);
                    SetSuccess(MobileStringKeys.QrAccountUpdated);
                    break;
                case QrAccountImportStatus.KeptBoth:
                    await LoadAccountsAsync(outcome.AccountId);
                    SetSuccess(MobileStringKeys.QrAccountKeptBoth);
                    break;
                case QrAccountImportStatus.DuplicateUnchanged:
                    await LoadAccountsAsync(outcome.AccountId);
                    SetNotification(
                        Get(MobileStringKeys.QrAccountDuplicate),
                        NotificationSeverity.Information);
                    break;
                case QrAccountImportStatus.Cancelled:
                    SetNotification(
                        Get(MobileStringKeys.QrImportCancelled),
                        NotificationSeverity.Information);
                    break;
                case QrAccountImportStatus.BulkImported:
                    if (outcome.ImportedCount > 0)
                        await LoadAccountsAsync(outcome.AccountId);
                    SetNotification(
                        FormatBulkImportMessage(outcome),
                        outcome.FailedCount > 0
                            ? NotificationSeverity.Warning
                            : NotificationSeverity.Success);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.QrScanFailed);
        }
        finally
        {
            EndSensitiveOperation(operation);
            payload = null;
            CompleteQrConflict(QrAccountConflictDecision.Cancel);
            CompleteImportConfirmation(false);
            IsBusy = false;
            TryStartAutomaticBiometricUnlock();
        }
    }

    private string FormatBulkImportMessage(QrAccountImportOutcome outcome)
    {
        if (outcome.HasMoreBatches)
        {
            return string.Format(
                Get(MobileStringKeys.QrBulkImportedMore),
                outcome.BatchIndex + 1,
                outcome.BatchSize,
                outcome.ImportedCount,
                outcome.DuplicateCount,
                outcome.FailedCount);
        }

        return string.Format(
            Get(MobileStringKeys.QrBulkImported),
            outcome.ImportedCount,
            outcome.DuplicateCount,
            outcome.FailedCount);
    }

    public async Task ShowQrAsync()
    {
        if (!CanEditAccounts() || SelectedAccount is null || HasQrImage) return;

        IsBusy = true;
        ClearNotification();
        ClearQrImage();
        try
        {
            var generated = await _accountQrCode.GenerateAsync(SelectedAccount.Id);
            if (generated.IsFailed)
            {
                SetError(MobileStringKeys.QrDisplayFailed);
                return;
            }

            using var png = generated.Value;
            _qrImage = _qrImageFactory.Create(png.Memory);
            OnPropertyChanged(nameof(QrImage));
            OnPropertyChanged(nameof(HasQrImage));
        }
        catch (Exception)
        {
            ClearQrImage();
            SetError(MobileStringKeys.QrDisplayFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ShowQrForAccountAsync(MobileAccountItem? account)
    {
        if (account is null || !CanEditAccounts() || !TrySelectAccount(account))
            return Task.CompletedTask;

        return ShowQrAsync();
    }

    public Task DismissQrAsync()
    {
        ClearQrImage();
        return Task.CompletedTask;
    }

    public async Task ExportBackupAsync()
    {
        if (!IsSettingsVisible || IsBusy) return;

        var password = BackupPassword;
        var confirmation = BackupPasswordConfirmation;
        BackupPassword = string.Empty;
        BackupPasswordConfirmation = string.Empty;
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmation))
        {
            SetError(MobileStringKeys.BackupPasswordRequired);
            return;
        }

        if (password.Length < _passwordValidation.MinimumLength)
        {
            SetNotification(
                string.Format(
                    Get(MobileStringKeys.BackupPasswordMinimumLength),
                    _passwordValidation.MinimumLength),
                NotificationSeverity.Error);
            return;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            SetError(MobileStringKeys.BackupPasswordMismatch);
            return;
        }

        IsBusy = true;
        MobileWritableDocument? document = null;
        using var operation = BeginSensitiveOperation();
        try
        {
            var accounts = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (accounts.IsFailed)
            {
                SetError(MobileStringKeys.BackupExportFailed);
                return;
            }

            document = await _documents.CreateEncryptedBackupAsync(
                string.Format(
                    Get(MobileStringKeys.BackupFileName),
                    _timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
                operation.Token);
            if (document is null) return;
            if (!_authorization.State.IsUnlocked || !IsSettingsVisible)
            {
                await document.DiscardAsync();
                SetError(MobileStringKeys.BackupRetryAfterUnlock);
                return;
            }

            var exported = await _exportService.ExportToEncryptedStreamAsync(
                accounts.Value,
                password,
                document.Stream,
                ExportFileFormat.Json,
                operation.Token);
            if (exported.IsFailed)
            {
                await document.DiscardAsync();
                SetError(MobileStringKeys.BackupExportFailed);
                return;
            }

            SetSuccess(MobileStringKeys.BackupExported);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (document is not null) await TryDiscardAsync(document);
        }
        catch (Exception)
        {
            var cleanupSucceeded = document is null || await TryDiscardAsync(document);
            SetError(cleanupSucceeded
                ? MobileStringKeys.BackupExportFailed
                : MobileStringKeys.BackupExportCleanupFailed);
        }
        finally
        {
            EndSensitiveOperation(operation);
            document?.Dispose();
            password = string.Empty;
            confirmation = string.Empty;
            IsBusy = false;
            TryStartAutomaticBiometricUnlock();
        }
    }

    public async Task ImportBackupAsync()
    {
        if (!IsSettingsVisible || IsBusy) return;

        var password = ImportPassword;
        ImportPassword = string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            SetError(MobileStringKeys.BackupPasswordRequired);
            return;
        }

        IsBusy = true;
        using var operation = BeginSensitiveOperation();
        try
        {
            using var document = await _documents.OpenEncryptedBackupAsync(operation.Token);
            if (document is null) return;
            if (!_authorization.State.IsUnlocked || !IsSettingsVisible)
            {
                SetError(MobileStringKeys.BackupRetryAfterUnlock);
                return;
            }

            var decoded = await _exportService.ImportFromEncryptedStreamAsync(
                password,
                document.Stream,
                operation.Token);
            if (decoded.IsFailed)
            {
                SetError(MobileStringKeys.BackupImportRejected);
                return;
            }

            var imported = await _accountImport.ImportAsync(
                decoded.Value,
                ImportConflictStrategy.SkipExisting,
                ConfirmImportAsync,
                operation.Token);
            if (imported.IsFailed)
            {
                SetError(MobileStringKeys.BackupImportFailed);
                return;
            }

            var outcome = imported.Value;
            if (outcome.Status == AccountImportStatus.Cancelled)
            {
                SetNotification(
                    Get(MobileStringKeys.BackupImportCancelled),
                    NotificationSeverity.Information);
                return;
            }

            if (outcome.Status != AccountImportStatus.Completed || outcome.Failed > 0)
            {
                SetError(MobileStringKeys.BackupImportFailed);
                return;
            }

            await LoadAccountsAsync();
            SetNotification(
                string.Format(
                    Get(MobileStringKeys.BackupImported),
                    outcome.Added,
                    outcome.Skipped),
                NotificationSeverity.Success);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.BackupImportFailed);
        }
        finally
        {
            EndSensitiveOperation(operation);
            password = string.Empty;
            CompleteImportConfirmation(false);
            IsBusy = false;
            TryStartAutomaticBiometricUnlock();
        }
    }

    public Task ResolveImportConfirmationAsync(bool confirmed)
    {
        CompleteImportConfirmation(confirmed);
        return Task.CompletedTask;
    }

    public Task ResolveQrConflictAsync(QrAccountConflictDecision decision)
    {
        CompleteQrConflict(decision);
        return Task.CompletedTask;
    }

    public Task BeginAddAsync()
    {
        if (!CanEditAccounts()) return Task.CompletedTask;
        ClearEditor();
        IsDeleteConfirmationVisible = false;
        CancelCodeRefresh();
        IsEditorVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task BeginEditAsync()
    {
        if (!CanEditAccounts() || SelectedAccount is null) return Task.CompletedTask;
        ClearQrImage();
        _editingAccountId = SelectedAccount.Id;
        EditorIssuer = SelectedAccount.Issuer;
        EditorAccountName = SelectedAccount.AccountName;
        EditorSecret = string.Empty;
        EditorPeriodSeconds = SelectedAccount.ConfiguredPeriodSeconds;
        IsDeleteConfirmationVisible = false;
        CancelCodeRefresh();
        IsEditorVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task BeginEditForAccountAsync(MobileAccountItem? account)
    {
        if (account is null || !CanEditAccounts() || !TrySelectAccount(account))
            return Task.CompletedTask;

        return BeginEditAsync();
    }

    public async Task SaveAccountAsync()
    {
        if (!IsEditorVisible || IsBusy) return;

        var issuer = EditorIssuer.Trim();
        var accountName = EditorAccountName.Trim();
        var enteredSecret = EditorSecret;
        EditorSecret = string.Empty;
        if (issuer.Length == 0)
        {
            SetError(MobileStringKeys.IssuerRequired);
            return;
        }

        IsBusy = true;
        string? secret = null;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (loaded.IsFailed)
            {
                SetError(MobileStringKeys.AccountSaveFailed);
                return;
            }

            var existing = _editingAccountId.HasValue
                ? loaded.Value.FirstOrDefault(account => account.ID == _editingAccountId.Value)
                : null;
            if (_editingAccountId.HasValue && existing is null)
            {
                SetError(MobileStringKeys.AccountSaveFailed);
                return;
            }

            secret = enteredSecret.Length > 0
                ? SecretValidation.NormalizeBase32Secret(enteredSecret)
                : existing?.Secret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                SetError(MobileStringKeys.SecretRequired);
                return;
            }

            if (!SecretValidation.IsValidBase32Secret(secret))
            {
                SetError(MobileStringKeys.SecretInvalid);
                return;
            }
            if (!TotpPeriodPolicy.IsSupported(EditorPeriodSeconds))
            {
                SetError(MobileStringKeys.TotpPeriodInvalid);
                return;
            }

            if (loaded.Value.Any(account =>
                    account.ID != _editingAccountId
                    && string.Equals(account.Issuer.Trim(), issuer, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        (account.AccountName ?? string.Empty).Trim(),
                        accountName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                SetError(MobileStringKeys.DuplicateAccount);
                return;
            }

            var updated = new Account(
                _editingAccountId ?? Guid.NewGuid(),
                issuer,
                secret,
                accountName.Length == 0 ? null : accountName,
                EditorPeriodSeconds);
            var saved = existing is null
                ? await _accountManager.AddNewAsync(updated)
                : await _accountManager.UpdateAsync(existing, updated);
            if (saved.IsFailed)
            {
                SetError(MobileStringKeys.AccountSaveFailed);
                return;
            }

            var savedId = updated.ID;
            ClearEditor();
            IsEditorVisible = false;
            await LoadAccountsAsync(savedId);
            SetSuccess(MobileStringKeys.AccountSaved);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.AccountSaveFailed);
        }
        finally
        {
            enteredSecret = string.Empty;
            secret = null;
            IsBusy = false;
        }
    }

    public Task CancelEditAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        ClearEditor();
        IsEditorVisible = false;
        ClearNotification();
        StartCodeRefresh();
        return Task.CompletedTask;
    }

    public Task BeginDeleteAsync()
    {
        if (!CanEditAccounts() || SelectedAccount is null) return Task.CompletedTask;
        ClearQrImage();
        _pendingDeleteAccountId = SelectedAccount.Id;
        _pendingDeleteDisplayName = SelectedAccount.DisplayName;
        IsDeleteConfirmationVisible = true;
        OnPropertyChanged(nameof(DeletePrompt));
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task BeginDeleteForAccountAsync(MobileAccountItem? account)
    {
        if (account is null || !CanEditAccounts() || !TrySelectAccount(account))
            return Task.CompletedTask;

        return BeginDeleteAsync();
    }

    public async Task ConfirmDeleteAsync()
    {
        if (!IsDeleteConfirmationVisible || !_pendingDeleteAccountId.HasValue || IsBusy) return;

        var accountId = _pendingDeleteAccountId.Value;
        IsBusy = true;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            var account = loaded.IsSuccess
                ? loaded.Value.FirstOrDefault(value => value.ID == accountId)
                : null;
            if (account is null || (await _accountManager.DeleteAsync(account)).IsFailed)
            {
                SetError(MobileStringKeys.AccountDeleteFailed);
                return;
            }

            IsDeleteConfirmationVisible = false;
            await LoadAccountsAsync();
            SetSuccess(MobileStringKeys.AccountDeleted);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.AccountDeleteFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CancelDeleteAsync()
    {
        if (!IsBusy) IsDeleteConfirmationVisible = false;
        return Task.CompletedTask;
    }

    public async Task CopyAccountCodeAsync(MobileAccountItem? account)
    {
        if (account is null || !CanEditAccounts() || !TrySelectAccount(account)) return;

        IsBusy = true;
        ClearNotification();
        var code = account.Code;
        try
        {
            if (code.Length == 0)
            {
                SetError(MobileStringKeys.CodeUnavailable);
                return;
            }

            await CopyCodeCoreAsync(code);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.CodeCopyFailed);
        }
        finally
        {
            code = string.Empty;
            IsBusy = false;
        }
    }

    public void OnEnteredBackground(bool lockImmediately)
    {
        if (!_authorization.State.IsUnlocked || _disposed) return;

        CancelCodeRefresh();
        ClearQrImage();
        if (!IsAppLockEnabled)
        {
            CancelBackgroundLockTimer();
            _backgroundedAtTimestamp = _timeProvider.GetTimestamp();
            return;
        }
        if (lockImmediately)
        {
            LockCore();
            return;
        }

        CancelBackgroundLockTimer();
        _backgroundedAtTimestamp = _timeProvider.GetTimestamp();
        _backgroundLockTimer = _timeProvider.CreateTimer(
            static state => ((MobileShellViewModel)state!).PostBackgroundLockCheck(),
            this,
            BackgroundLockGracePeriod,
            Timeout.InfiniteTimeSpan);
    }

    public void OnReturnedToForeground()
    {
        if (_disposed) return;
        if (!IsAppLockEnabled)
        {
            _backgroundedAtTimestamp = null;
            CancelBackgroundLockTimer();
            if (_authorization.State.IsUnlocked) StartCodeRefresh();
            return;
        }
        if (!_backgroundedAtTimestamp.HasValue)
        {
            RequestAutomaticBiometricUnlock();
            return;
        }

        var elapsed = _timeProvider.GetElapsedTime(_backgroundedAtTimestamp.Value);
        _backgroundedAtTimestamp = null;
        CancelBackgroundLockTimer();
        if (elapsed >= BackgroundLockGracePeriod)
        {
            LockCore();
            RequestAutomaticBiometricUnlock();
            return;
        }

        if (_authorization.State.IsUnlocked)
            StartCodeRefresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sensitiveOperationLifetime?.Cancel();
        CancelNotificationLifetime();
        CancelBackgroundLockTimer();
        CancelCodeRefresh();
        ClearQrImage();
        ClearEditor();
        ClearPasswordInputs();
        Accounts.Clear();
        _authorization.Lock();
        CompleteQrConflict(QrAccountConflictDecision.Cancel);
        CompleteImportConfirmation(false);
    }

    private async Task LoadAccountsAsync(Guid? selectedId = null)
    {
        var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
        if (loaded.IsFailed)
        {
            SetError(MobileStringKeys.LoadingAccountsFailed);
            return;
        }

        _allAccounts.Clear();
        foreach (var account in loaded.Value)
        {
            _allAccounts.Add(new MobileAccountItem(
                account.ID,
                account.Issuer,
                account.AccountName ?? string.Empty,
                account.PeriodSeconds,
                FormatCustomPeriod(account.PeriodSeconds)));
        }

        ApplyAccountFilter(selectedId);
    }

    private void ApplyAccountFilter(Guid? preferredSelection = null)
    {
        var selectedId = preferredSelection ?? SelectedAccount?.Id;
        var query = SearchText.Trim();
        var matches = query.Length == 0
            ? _allAccounts
            : _allAccounts.Where(account =>
                account.Issuer.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || account.AccountName.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        Accounts.Clear();
        foreach (var account in matches) Accounts.Add(account);

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasNoSearchResults));
        SelectedAccount = selectedId.HasValue
            ? Accounts.FirstOrDefault(account => account.Id == selectedId.Value)
                ?? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault();
        NotifyCommands();
        StartCodeRefresh();
    }

    private bool TrySelectAccount(MobileAccountItem account)
    {
        if (!Accounts.Any(value => value.Id == account.Id)) return false;
        SelectedAccount = account;
        return true;
    }

    private async Task CopyCodeCoreAsync(string code)
    {
        try
        {
            var seconds = Math.Max(1, _settings.Current.ClearClipboardSeconds);
            var result = _settings.Current.ClearClipboardEnabled
                ? await _clipboard.CopyAndScheduleClearAsync(code, TimeSpan.FromSeconds(seconds))
                : await _clipboard.CopyAsync(code);
            if (result.IsFailed)
            {
                SetError(MobileStringKeys.CodeCopyFailed);
                return;
            }

            if (_settings.Current.ClearClipboardEnabled)
            {
                SetTransientNotification(
                    string.Format(Get(MobileStringKeys.CodeCopiedWithClear), seconds),
                    NotificationSeverity.Success,
                    CopiedNotificationDuration);
            }
            else
            {
                SetTransientNotification(
                    Get(MobileStringKeys.CodeCopied),
                    NotificationSeverity.Success,
                    CopiedNotificationDuration);
            }
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.CodeCopyFailed);
        }
    }

    private void StartCodeRefresh()
    {
        CancelCodeRefresh();
        if (Accounts.Count == 0 || !IsAccountListVisible) return;

        var lifetime = new CancellationTokenSource();
        _codeLifetime = lifetime;
        _ = RunCodeRefreshAsync(lifetime);
    }

    private async Task RunCodeRefreshAsync(CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var visibleAccounts = Accounts.ToArray();
                var secondsUntilRefresh = int.MaxValue;
                foreach (var account in visibleAccounts)
                {
                    var generated = await _accountTotp.GenerateAsync(account.Id);
                    if (lifetime.IsCancellationRequested) return;
                    if (generated.IsFailed)
                    {
                        account.ClearCode();
                        SetError(MobileStringKeys.CodeUnavailable);
                        continue;
                    }

                    var remaining = Math.Max(1, generated.Value.RemainingSeconds);
                    account.UpdateCode(
                        generated.Value.Code,
                        remaining,
                        generated.Value.PeriodSeconds);
                    secondsUntilRefresh = Math.Min(secondsUntilRefresh, remaining);
                }

                if (secondsUntilRefresh == int.MaxValue) return;
                for (var second = 0;
                     second < secondsUntilRefresh && !lifetime.IsCancellationRequested;
                     second++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), lifetime.Token);
                    foreach (var account in visibleAccounts) account.Tick();
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            foreach (var account in Accounts) account.ClearCode();
            SetError(MobileStringKeys.CodeUnavailable);
        }
        finally
        {
            if (ReferenceEquals(_codeLifetime, lifetime)) _codeLifetime = null;
            lifetime.Dispose();
        }
    }

    private void LockCore()
    {
        if (_disposed || !IsAppLockEnabled) return;
        _backgroundedAtTimestamp = null;
        CancelBackgroundLockTimer();
        _sensitiveOperationLifetime?.Cancel();
        CancelNotificationLifetime();
        _authorization.Lock();
        CancelCodeRefresh();
        ClearEditor();
        IsEditorVisible = false;
        IsDeleteConfirmationVisible = false;
        ClearPasswordInputs();
        SelectedAccount = null;
        Accounts.Clear();
        _allAccounts.Clear();
        SearchText = string.Empty;
        _isSettingsVisible = false;
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasNoSearchResults));
        IsBiometricEnrollmentVisible = false;
        ClearNotification();
        SetScreen(_authorization.State.IsConfigured
            ? MobileScreen.Unlock
            : MobileScreen.Setup);
    }

    private void RequestAutomaticBiometricUnlock()
    {
        _automaticBiometricUnlockPending = true;
        TryStartAutomaticBiometricUnlock();
    }

    private void TryStartAutomaticBiometricUnlock()
    {
        if (!_automaticBiometricUnlockPending
            || !IsBiometricUnlockVisible
            || IsBusy
            || _disposed)
        {
            return;
        }

        _automaticBiometricUnlockPending = false;
        _ = BiometricUnlockAsync();
    }

    private void CancelCodeRefresh()
    {
        var lifetime = _codeLifetime;
        _codeLifetime = null;
        lifetime?.Cancel();
        foreach (var account in _allAccounts) account.ClearCode();
    }

    private void PostBackgroundLockCheck()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_backgroundedAtTimestamp.HasValue) return;
            if (_timeProvider.GetElapsedTime(_backgroundedAtTimestamp.Value)
                >= BackgroundLockGracePeriod)
            {
                LockCore();
            }
        });
    }

    private void CancelBackgroundLockTimer()
    {
        _backgroundLockTimer?.Dispose();
        _backgroundLockTimer = null;
    }

    private void ClearEditor()
    {
        _editingAccountId = null;
        EditorIssuer = string.Empty;
        EditorAccountName = string.Empty;
        EditorSecret = string.Empty;
        EditorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
        IsAdvancedOptionsExpanded = false;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSecretPlaceholder));
    }

    private void ClearPasswordInputs()
    {
        SetupPassword = string.Empty;
        SetupConfirmation = string.Empty;
        UnlockPassword = string.Empty;
        BiometricRecoveryPassword = string.Empty;
        AppLockRecoveryPassword = string.Empty;
        BackupPassword = string.Empty;
        BackupPasswordConfirmation = string.Empty;
        ImportPassword = string.Empty;
    }

    private void ClearQrImage()
    {
        var image = _qrImage;
        _qrImage = null;
        image?.Dispose();
        OnPropertyChanged(nameof(QrImage));
        OnPropertyChanged(nameof(HasQrImage));
        _dismissQrCommand?.NotifyCanExecuteChanged();
    }

    private bool CanEditAccounts() =>
        IsAccountListVisible
        && !IsBusy
        && !IsDeleteConfirmationVisible
        && !IsQrConflictVisible;

    private async Task<QrAccountConflictDecision> ResolveQrConflictAsync(
        QrAccountConflict conflict,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<QrAccountConflictDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _qrConflictCompletion = completion;
        _qrConflictDisplayName = string.IsNullOrWhiteSpace(conflict.AccountName)
            ? conflict.Issuer
            : $"{conflict.Issuer}: {conflict.AccountName}";
        OnPropertyChanged(nameof(QrConflictPrompt));
        IsQrConflictVisible = true;

        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            return await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(_qrConflictCompletion, completion))
            {
                _qrConflictCompletion = null;
                _qrConflictDisplayName = string.Empty;
                IsQrConflictVisible = false;
                OnPropertyChanged(nameof(QrConflictPrompt));
            }
        }
    }

    private void CompleteQrConflict(QrAccountConflictDecision decision) =>
        _qrConflictCompletion?.TrySetResult(decision);

    private Task<bool> ConfirmImportAsync(
        AccountImportPreview preview,
        CancellationToken cancellationToken)
        => ShowImportConfirmationAsync(
            string.Format(
                Get(MobileStringKeys.ImportConfirmation),
                preview.TotalCount,
                preview.ConflictCount),
            cancellationToken);

    private Task<bool> ConfirmQrMigrationAsync(
        int accountCount,
        CancellationToken cancellationToken)
        => ShowImportConfirmationAsync(
            string.Format(
                Get(MobileStringKeys.QrMigrationConfirmation),
                accountCount),
            cancellationToken);

    private async Task<bool> ShowImportConfirmationAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _importConfirmationCompletion = completion;
        ImportConfirmationText = message;
        IsImportConfirmationVisible = true;

        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            return await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(_importConfirmationCompletion, completion))
            {
                _importConfirmationCompletion = null;
                ImportConfirmationText = string.Empty;
                IsImportConfirmationVisible = false;
            }
        }
    }

    private void CompleteImportConfirmation(bool confirmed) =>
        _importConfirmationCompletion?.TrySetResult(confirmed);

    private static async Task<bool> TryDiscardAsync(MobileWritableDocument document)
    {
        try
        {
            await document.DiscardAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private CancellationTokenSource BeginSensitiveOperation()
    {
        var lifetime = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _sensitiveOperationLifetime, lifetime);
        previous?.Cancel();
        return lifetime;
    }

    private void EndSensitiveOperation(CancellationTokenSource lifetime)
    {
        if (ReferenceEquals(
            Interlocked.CompareExchange(ref _sensitiveOperationLifetime, null, lifetime),
            lifetime))
        {
            return;
        }
    }

    private void NotifyUnlockedSectionChanged()
    {
        OnPropertyChanged(nameof(IsAccountListVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(IsBiometricSetupAvailable));
        OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
        OnPropertyChanged(nameof(IsBiometricUnavailable));
        NotifyCommands();
    }

    private void NotifyAppLockChanged()
    {
        OnPropertyChanged(nameof(IsAppLockEnabled));
        OnPropertyChanged(nameof(IsAppLockDisabled));
        OnPropertyChanged(nameof(IsManualLockVisible));
        OnPropertyChanged(nameof(IsBiometricSetupAvailable));
        OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
        NotifyCommands();
    }

    private void NotifyLocalizedTextChanged()
    {
        foreach (var propertyName in LocalizedTextProperties)
            OnPropertyChanged(propertyName);

        foreach (var account in _allAccounts)
            account.UpdateCustomPeriodLabel(FormatCustomPeriod(account.ConfiguredPeriodSeconds));

        // Language buttons bind to these computed selection properties. They are
        // state, not localized text, but must refresh together with the catalog.
        OnPropertyChanged(nameof(IsEnglishLanguageSelected));
        OnPropertyChanged(nameof(IsGermanLanguageSelected));
        OnPropertyChanged(nameof(IsFrenchLanguageSelected));
        OnPropertyChanged(nameof(IsSpanishLanguageSelected));

        NotifyCommands();
    }

    private string FormatCustomPeriod(int periodSeconds) => string.Format(
        Get(MobileStringKeys.CustomPeriodFormat),
        periodSeconds);

    private void FailStartup()
    {
        _startupFailed = true;
        SetScreen(MobileScreen.Starting);
        SetError(MobileStringKeys.StartupFailed);
        OnPropertyChanged(nameof(CanRetry));
    }

    private void SetScreen(MobileScreen screen)
    {
        if (_screen == screen) return;
        _screen = screen;
        OnPropertyChanged(nameof(IsStartingVisible));
        OnPropertyChanged(nameof(IsSetupVisible));
        OnPropertyChanged(nameof(IsUnlockVisible));
        OnPropertyChanged(nameof(IsAccountsVisible));
        OnPropertyChanged(nameof(IsManualLockVisible));
        NotifyUnlockedSectionChanged();
        OnPropertyChanged(nameof(IsBiometricUnlockVisible));
    }

    private void SetError(string key) =>
        SetNotification(Get(key), NotificationSeverity.Error);

    private void SetSuccess(string key) =>
        SetNotification(Get(key), NotificationSeverity.Success);

    private void SetNotification(string text, NotificationSeverity severity)
    {
        CancelNotificationLifetime();
        NotificationSeverity = severity;
        NotificationText = text;
    }

    private void SetTransientNotification(
        string text,
        NotificationSeverity severity,
        TimeSpan duration)
    {
        SetNotification(text, severity);
        if (string.IsNullOrWhiteSpace(text)) return;

        var lifetime = new CancellationTokenSource();
        _notificationLifetime = lifetime;
        _ = ClearNotificationAfterDelayAsync(text, duration, lifetime);
    }

    private void ClearNotification()
    {
        CancelNotificationLifetime();
        NotificationText = string.Empty;
        NotificationSeverity = NotificationSeverity.Information;
    }

    private async Task ClearNotificationAfterDelayAsync(
        string expectedText,
        TimeSpan duration,
        CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(duration, lifetime.Token);
            if (ReferenceEquals(_notificationLifetime, lifetime)
                && string.Equals(NotificationText, expectedText, StringComparison.Ordinal))
            {
                NotificationText = string.Empty;
                NotificationSeverity = NotificationSeverity.Information;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_notificationLifetime, lifetime))
                _notificationLifetime = null;
            lifetime.Dispose();
        }
    }

    private void CancelNotificationLifetime()
    {
        var lifetime = _notificationLifetime;
        _notificationLifetime = null;
        lifetime?.Cancel();
    }

    private void ClearErrorNotification()
    {
        if (NotificationSeverity == NotificationSeverity.Error) ClearNotification();
    }

    private string Get(string key) => _strings.Get(key);

    private void NotifyCommands()
    {
        _initializeCommand.NotifyCanExecuteChanged();
        _configureCommand.NotifyCanExecuteChanged();
        _unlockCommand.NotifyCanExecuteChanged();
        _biometricUnlockCommand.NotifyCanExecuteChanged();
        _beginBiometricEnrollmentCommand.NotifyCanExecuteChanged();
        _enableBiometricCommand.NotifyCanExecuteChanged();
        _cancelBiometricEnrollmentCommand.NotifyCanExecuteChanged();
        _beginDisableAppLockCommand.NotifyCanExecuteChanged();
        _confirmDisableAppLockCommand.NotifyCanExecuteChanged();
        _cancelDisableAppLockCommand.NotifyCanExecuteChanged();
        _enableAppLockCommand.NotifyCanExecuteChanged();
        _lockCommand.NotifyCanExecuteChanged();
        _showAccountsCommand.NotifyCanExecuteChanged();
        _showSettingsCommand.NotifyCanExecuteChanged();
        _beginAddCommand.NotifyCanExecuteChanged();
        _saveAccountCommand.NotifyCanExecuteChanged();
        _cancelEditCommand.NotifyCanExecuteChanged();
        _confirmDeleteCommand.NotifyCanExecuteChanged();
        _cancelDeleteCommand.NotifyCanExecuteChanged();
        _scanQrCommand.NotifyCanExecuteChanged();
        _importGoogleQrCommand.NotifyCanExecuteChanged();
        _updateQrConflictCommand.NotifyCanExecuteChanged();
        _keepBothQrConflictCommand.NotifyCanExecuteChanged();
        _cancelQrConflictCommand.NotifyCanExecuteChanged();
        _dismissQrCommand.NotifyCanExecuteChanged();
        _exportBackupCommand.NotifyCanExecuteChanged();
        _importBackupCommand.NotifyCanExecuteChanged();
        _confirmImportCommand.NotifyCanExecuteChanged();
        _cancelImportCommand.NotifyCanExecuteChanged();
        _selectEnglishLanguageCommand.NotifyCanExecuteChanged();
        _selectGermanLanguageCommand.NotifyCanExecuteChanged();
        _selectFrenchLanguageCommand.NotifyCanExecuteChanged();
        _selectSpanishLanguageCommand.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static readonly string[] LocalizedTextProperties =
    [
        nameof(StartingText),
        nameof(AppTitle),
        nameof(RetryText),
        nameof(SetupTitle),
        nameof(SetupDescription),
        nameof(MasterPasswordText),
        nameof(ConfirmPasswordText),
        nameof(CreateVaultText),
        nameof(UnlockTitle),
        nameof(UnlockDescription),
        nameof(UnlockText),
        nameof(AccountsTitle),
        nameof(NoAccountsText),
        nameof(AddAccountText),
        nameof(EditAccountText),
        nameof(DeleteAccountText),
        nameof(LockText),
        nameof(IssuerText),
        nameof(AccountNameText),
        nameof(AdvancedOptionsText),
        nameof(TotpPeriodText),
        nameof(TotpPeriodHelpText),
        nameof(SaveText),
        nameof(CancelText),
        nameof(CopyCodeText),
        nameof(DeleteConfirmTitle),
        nameof(DeleteText),
        nameof(DeletePrompt),
        nameof(BiometricUnlockText),
        nameof(BiometricSetupTitle),
        nameof(BiometricSetupDescription),
        nameof(BiometricEnableText),
        nameof(BiometricEnabledText),
        nameof(BiometricUnavailableText),
        nameof(CodesText),
        nameof(SettingsText),
        nameof(LanguageText),
        nameof(EnglishLanguageText),
        nameof(GermanLanguageText),
        nameof(FrenchLanguageText),
        nameof(SpanishLanguageText),
        nameof(SecurityText),
        nameof(AppLockTitleText),
        nameof(AppLockEnabledDescriptionText),
        nameof(AppLockDisabledDescriptionText),
        nameof(DisableAppLockText),
        nameof(EnableAppLockText),
        nameof(DisableAppLockWarningText),
        nameof(SearchAccountsText),
        nameof(NoSearchResultsText),
        nameof(AccountSwipeHintText),
        nameof(ScanQrText),
        nameof(ImportExportText),
        nameof(ImportGoogleQrText),
        nameof(ImportGoogleQrDescriptionText),
        nameof(QrConflictTitle),
        nameof(QrConflictPrompt),
        nameof(UpdateExistingText),
        nameof(KeepBothText),
        nameof(ShowQrText),
        nameof(DismissQrText),
        nameof(QrPrivacyNoticeText),
        nameof(BackupTitle),
        nameof(BackupDescription),
        nameof(BackupPasswordText),
        nameof(ConfirmBackupPasswordText),
        nameof(ExportBackupText),
        nameof(ImportBackupText),
        nameof(ImportConfirmationTitle),
        nameof(ConfirmImportText),
        nameof(EditorTitle),
        nameof(EditorSecretPlaceholder)
    ];

    private enum MobileScreen
    {
        Starting,
        Setup,
        Unlock,
        Accounts
    }
}
