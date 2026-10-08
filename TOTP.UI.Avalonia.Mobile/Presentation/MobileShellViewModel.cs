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
using TOTP.Core.Icons;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;
using TOTP.Avalonia.Shared.Branding;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed class MobileShellViewModel :
    INotifyPropertyChanged,
    IMobileLifecycleSink,
    IDisposable
{
    private static readonly TimeSpan BackgroundLockGracePeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NotificationDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CopyConfirmationDuration = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ImportProgressDelay = TimeSpan.FromMilliseconds(1500);
    private const int FullListCodeRefreshLimit = 50;

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
    private readonly IBrandIconResolver _brandIconResolver;
    private readonly IBrandIconPackService? _brandIconPackService;
    private readonly IAppearanceSettingsService? _appearanceSettingsService;
    private readonly MobileAsyncCommand _initializeCommand;
    private readonly MobileAsyncCommand _configureCommand;
    private readonly MobileAsyncCommand _unlockCommand;
    private readonly MobileAsyncCommand _biometricUnlockCommand;
    private readonly MobileAsyncCommand _beginBiometricEnrollmentCommand;
    private readonly MobileAsyncCommand _beginDeviceCredentialEnrollmentCommand;
    private readonly MobileAsyncCommand _selectPasswordUnlockCommand;
    private readonly MobileAsyncCommand _enableBiometricCommand;
    private readonly MobileAsyncCommand _cancelBiometricEnrollmentCommand;
    private readonly MobileAsyncCommand _toggleAppLockCommand;
    private readonly MobileAsyncCommand _beginDisableAppLockCommand;
    private readonly MobileAsyncCommand _confirmDisableAppLockCommand;
    private readonly MobileAsyncCommand _cancelDisableAppLockCommand;
    private readonly MobileAsyncCommand _enableAppLockCommand;
    private readonly MobileAsyncCommand _changeMasterPasswordCommand;
    private readonly MobileAsyncCommand _lockCommand;
    private readonly MobileAsyncCommand _showAccountsCommand;
    private readonly MobileAsyncCommand _showSettingsCommand;
    private readonly MobileAsyncCommand _showSettingsCategoriesCommand;
    private readonly MobileAsyncCommand _showAppearanceSettingsCommand;
    private readonly MobileAsyncCommand _showBrandIconSettingsCommand;
    private readonly MobileAsyncCommand _showSecuritySettingsCommand;
    private readonly MobileAsyncCommand _showBackupSettingsCommand;
    private readonly MobileAsyncCommand _showImportExportSettingsCommand;
    private readonly MobileAsyncCommand _showMiscSettingsCommand;
    private readonly MobileAsyncCommand _showFaqSettingsCommand;
    private readonly MobileAsyncCommand _showImportFormatsFaqCommand;
    private readonly MobileAsyncCommand _clearSearchCommand;
    private readonly MobileAsyncCommand _clearGroupEditorSearchCommand;
    private readonly MobileAsyncCommand _toggleFavoritesFilterCommand;
    private readonly MobileAsyncCommand _clearGroupFilterCommand;
    private readonly MobileAsyncCommand _beginAddGroupCommand;
    private readonly MobileAsyncCommand _saveGroupCommand;
    private readonly MobileAsyncCommand _cancelGroupEditCommand;
    private readonly MobileAsyncCommand _beginDeleteGroupCommand;
    private readonly MobileAsyncCommand _confirmDeleteGroupCommand;
    private readonly MobileAsyncCommand _cancelDeleteGroupCommand;
    private readonly MobileAsyncCommand _beginAddCommand;
    private readonly MobileAsyncCommand _saveAccountCommand;
    private readonly MobileAsyncCommand _cancelEditCommand;
    private readonly MobileAsyncCommand _saveAccountAndNavigateBackCommand;
    private readonly MobileAsyncCommand _discardAccountChangesCommand;
    private readonly MobileAsyncCommand _cancelAccountNavigationCommand;
    private readonly MobileAsyncCommand _clearEditorPeriodCommand;
    private readonly MobileAsyncCommand _importCustomIconCommand;
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
    private readonly MobileAsyncCommand _importAccountFileCommand;
    private readonly MobileAsyncCommand _importBrandIconsCommand;
    private readonly MobileAsyncCommand _resetBrandIconsCommand;
    private readonly MobileAsyncCommand _confirmImportCommand;
    private readonly MobileAsyncCommand _cancelImportCommand;
    private readonly MobileAsyncCommand _skipAllBackupConflictsCommand;
    private readonly MobileAsyncCommand _replaceAllBackupConflictsCommand;
    private readonly MobileAsyncCommand _confirmBackupConflictResolutionCommand;
    private readonly MobileAsyncCommand _cancelBackupConflictResolutionCommand;
    private readonly MobileAsyncCommand _selectEnglishLanguageCommand;
    private readonly MobileAsyncCommand _selectGermanLanguageCommand;
    private readonly MobileAsyncCommand _selectFrenchLanguageCommand;
    private readonly MobileAsyncCommand _selectSpanishLanguageCommand;
    private readonly MobileAsyncCommand _selectSystemThemeCommand;
    private readonly MobileAsyncCommand _selectDarkThemeCommand;
    private readonly MobileAsyncCommand _selectLightThemeCommand;

    private MobileScreen _screen = MobileScreen.Starting;
    private bool _isBusy;
    private bool _hasLoadedAccounts;
    private bool _startupFailed;
    private string _notificationText = string.Empty;
    private NotificationSeverity _notificationSeverity = NotificationSeverity.Information;
    private string _setupPassword = string.Empty;
    private string _setupConfirmation = string.Empty;
    private string _unlockPassword = string.Empty;
    private bool _isBiometricAvailable;
    private bool _isDeviceCredentialAvailable;
    private bool _isBiometricEnabled;
    private bool _isBiometricEnrollmentVisible;
    private string _biometricRecoveryPassword = string.Empty;
    private string _appLockRecoveryPassword = string.Empty;
    private string _currentMasterPassword = string.Empty;
    private string _newMasterPassword = string.Empty;
    private string _newMasterPasswordConfirmation = string.Empty;
    private bool _isDisableAppLockConfirmationVisible;
    private PreferredUnlockMethod _pendingUnlockMethod =
        PreferredUnlockMethod.PlatformQuickUnlock;
    private bool _isReenablingAppLock;
    private bool _isSettingsVisible;
    private MobileSettingsCategory _settingsCategory;
    private bool _showIssuerLogo = true;
    private long _showIssuerLogoRevision;
    private string _searchText = string.Empty;
    private bool _showFavoritesOnly;
    private int _favoriteCount;
    private Guid? _selectedGroupId;
    private readonly List<MobileAccountItem> _allAccounts = [];
    private readonly RangeObservableCollection<MobileAccountItem> _accounts = [];
    private readonly RangeObservableCollection<MobileAccountGroupItem> _groups = [];
    private readonly HashSet<Guid> _realizedAccountIds = [];
    private bool _isGroupEditorVisible;
    private bool _isDeleteGroupConfirmationVisible;
    private Guid? _editingGroupId;
    private string _groupEditorName = string.Empty;
    private string _groupEditorSearchText = string.Empty;
    private string _groupEditorMessage = string.Empty;
    private readonly List<MobileGroupAccountSelection> _allGroupEditorAccounts = [];
    private readonly RangeObservableCollection<MobileGroupAccountSelection> _groupEditorAccounts = [];
    private IReadOnlyList<MobileGroupColorOption> _groupColorOptions = [];
    private MobileGroupColorOption? _selectedGroupColor;
    private MobileAccountItem? _selectedAccount;
    private MobileAccountRevealRequest? _accountRevealRequest;
    private int _accountRevealRevision;
    private MobileAccountGroupRevealRequest? _groupRevealRequest;
    private int _groupRevealRevision;
    private MobileResumeTarget _resumeTarget;
    private MobileSettingsCategory _resumeSettingsCategory;
    private Guid? _resumeEntityId;
    private Guid? _resumeSelectedGroupId;
    private bool _resumeFavoritesFilter;
    private bool _isEditorVisible;
    private bool _isAccountEditorExitConfirmationVisible;
    private bool _isDeleteConfirmationVisible;
    private Guid? _pendingDeleteAccountId;
    private string _pendingDeleteDisplayName = string.Empty;
    private Guid? _editingAccountId;
    private string _editorIssuer = string.Empty;
    private string _editorAccountName = string.Empty;
    private string _editorSecret = string.Empty;
    private int? _editorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
    private bool _editorIsFavorite;
    private bool _isAdvancedOptionsExpanded;
    private IReadOnlyList<MobileBrandIconOption> _editorBrandIconOptions = [];
    private MobileBrandIconOption? _selectedEditorBrandIconOption;
    private AccountEditorSnapshot? _accountEditorBaseline;
    private string _editorIssuerMessage = string.Empty;
    private string _editorSecretMessage = string.Empty;
    private string _editorPeriodMessage = string.Empty;
    private bool _isQrConflictVisible;
    private string _qrConflictDisplayName = string.Empty;
    private TaskCompletionSource<QrAccountConflictDecision>? _qrConflictCompletion;
    private MobileQrImageHandle? _qrImage;
    private string _backupPassword = string.Empty;
    private string _backupPasswordConfirmation = string.Empty;
    private string _importPassword = string.Empty;
    private bool _isImportConfirmationVisible;
    private string _importConfirmationText = string.Empty;
    private bool _isImportConfirmationAcknowledgementOnly;
    private bool _isImportProgressVisible;
    private string _importProgressText = string.Empty;
    private TaskCompletionSource<bool>? _importConfirmationCompletion;
    private bool _isBackupConflictResolutionVisible;
    private string _backupConflictResolutionText = string.Empty;
    private TaskCompletionSource<AccountImportResolution?>? _backupConflictResolutionCompletion;
    private CancellationTokenSource? _sensitiveOperationLifetime;
    private CancellationTokenSource? _codeLifetime;
    private CancellationTokenSource? _notificationLifetime;
    private CancellationTokenSource? _copyConfirmationLifetime;
    private CancellationTokenSource? _importProgressLifetime;
    private MobileAccountItem? _copyConfirmationAccount;
    private long? _backgroundedAtTimestamp;
    private ITimer? _backgroundLockTimer;
    private bool _automaticBiometricUnlockPending;
    private bool _isAccountListScrolling;
    private bool _isFavoriteUpdateInProgress;
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
        TimeProvider timeProvider,
        IBrandIconResolver? brandIconResolver = null,
        IBrandIconPackService? brandIconPackService = null,
        IAppearanceSettingsService? appearanceSettingsService = null)
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
        _brandIconResolver = brandIconResolver ?? FallbackBrandIconResolver.Instance;
        _brandIconPackService = brandIconPackService;
        _appearanceSettingsService = appearanceSettingsService;
        _showIssuerLogo = _brandIconPackService?.ShowIssuerLogo ?? true;
        _brandIconResolver.CatalogChanged += BrandCatalogChanged;

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
            () => CanSelectUnlockMethod(PreferredUnlockMethod.PlatformQuickUnlock));
        _beginDeviceCredentialEnrollmentCommand = new MobileAsyncCommand(
            BeginDeviceCredentialEnrollmentAsync,
            () => CanSelectUnlockMethod(PreferredUnlockMethod.PlatformDeviceCredential));
        _selectPasswordUnlockCommand = new MobileAsyncCommand(
            SelectPasswordUnlockAsync,
            () => CanSelectUnlockMethod(PreferredUnlockMethod.Password));
        _enableBiometricCommand = new MobileAsyncCommand(
            EnableBiometricAsync,
            () => IsBiometricEnrollmentVisible
                && !IsBusy
                && BiometricRecoveryPassword.Length > 0);
        _cancelBiometricEnrollmentCommand = new MobileAsyncCommand(
            CancelBiometricEnrollmentAsync,
            () => IsBiometricEnrollmentVisible && !IsBusy);
        _toggleAppLockCommand = new MobileAsyncCommand(
            ToggleAppLockAsync,
            () => IsSettingsVisible && !IsBusy);
        _beginDisableAppLockCommand = new MobileAsyncCommand(
            BeginDisableAppLockAsync,
            () => !IsBusy);
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
        _changeMasterPasswordCommand = new MobileAsyncCommand(
            ChangeMasterPasswordAsync,
            () => IsSecuritySettingsVisible && !IsBusy);
        _lockCommand = new MobileAsyncCommand(
            LockAsync,
            () => IsAccountsVisible && IsAppLockEnabled);
        _showAccountsCommand = new MobileAsyncCommand(
            ShowAccountsAsync,
            () => IsAccountsVisible && IsSettingsVisible && !IsBusy);
        _showSettingsCommand = new MobileAsyncCommand(
            ShowSettingsAsync,
            () => IsAccountsVisible
                && !IsSettingsVisible
                && !IsEditorVisible
                && !IsGroupEditorVisible
                && !IsBusy);
        _showSettingsCategoriesCommand = new MobileAsyncCommand(
            ShowSettingsCategoriesAsync,
            () => IsSettingsCategoryDetailVisible && !IsBusy);
        _showAppearanceSettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.Appearance);
        _showBrandIconSettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.BrandIcons);
        _showSecuritySettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.Security);
        _showBackupSettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.Backups);
        _showImportExportSettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.ImportExport);
        _showMiscSettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.Miscellaneous);
        _showFaqSettingsCommand = SettingsCategoryCommand(MobileSettingsCategory.Faq);
        _showImportFormatsFaqCommand = new MobileAsyncCommand(
            () => ShowSettingsCategoryAsync(MobileSettingsCategory.Faq),
            () => IsImportExportSettingsVisible && !IsBusy);
        _clearSearchCommand = new MobileAsyncCommand(
            ClearSearchAsync,
            () => HasSearchText && IsAccountListVisible && !IsBusy);
        _clearGroupEditorSearchCommand = new MobileAsyncCommand(
            ClearGroupEditorSearchAsync,
            () => HasGroupEditorSearchText && IsGroupEditorVisible && !IsBusy);
        _toggleFavoritesFilterCommand = new MobileAsyncCommand(
            ToggleFavoritesFilterAsync,
            () => HasFavoriteAccounts && IsAccountListVisible && !IsBusy);
        _clearGroupFilterCommand = new MobileAsyncCommand(
            ClearGroupFilterAsync,
            () => HasSelectedGroup && IsAccountListVisible && !IsBusy);
        _beginAddGroupCommand = new MobileAsyncCommand(
            BeginAddGroupAsync,
            () => IsAccountListVisible && _allAccounts.Count > 0 && !IsBusy);
        _saveGroupCommand = new MobileAsyncCommand(
            SaveGroupAsync,
            () => IsGroupEditorVisible && !IsBusy);
        _cancelGroupEditCommand = new MobileAsyncCommand(
            CancelGroupEditAsync,
            () => IsGroupEditorVisible && !IsBusy);
        _beginDeleteGroupCommand = new MobileAsyncCommand(
            BeginDeleteGroupAsync,
            () => IsGroupEditorVisible && IsEditingGroup && !IsBusy);
        _confirmDeleteGroupCommand = new MobileAsyncCommand(
            ConfirmDeleteGroupAsync,
            () => IsDeleteGroupConfirmationVisible && !IsBusy);
        _cancelDeleteGroupCommand = new MobileAsyncCommand(
            CancelDeleteGroupAsync,
            () => IsDeleteGroupConfirmationVisible && !IsBusy);
        _beginAddCommand = new MobileAsyncCommand(BeginAddAsync, CanEditAccounts);
        _saveAccountCommand = new MobileAsyncCommand(
            SaveAccountAsync,
            () => IsEditorVisible && !IsBusy);
        _cancelEditCommand = new MobileAsyncCommand(
            CancelEditAsync,
            () => IsEditorVisible && !IsBusy);
        _saveAccountAndNavigateBackCommand = new MobileAsyncCommand(
            SaveAccountAndNavigateBackAsync,
            () => IsAccountEditorExitConfirmationVisible && !IsBusy);
        _discardAccountChangesCommand = new MobileAsyncCommand(
            DiscardAccountChangesAsync,
            () => IsAccountEditorExitConfirmationVisible && !IsBusy);
        _cancelAccountNavigationCommand = new MobileAsyncCommand(
            CancelAccountNavigationAsync,
            () => IsAccountEditorExitConfirmationVisible && !IsBusy);
        _clearEditorPeriodCommand = new MobileAsyncCommand(
            ClearEditorPeriodAsync,
            () => IsEditorVisible && !IsBusy && EditorPeriodSeconds.HasValue);
        _importCustomIconCommand = new MobileAsyncCommand(
            ImportCustomIconAsync,
            () => IsEditorVisible
                && IsEditingExistingAccount
                && !IsBusy
                && _brandIconPackService is not null);
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
        _importAccountFileCommand = new MobileAsyncCommand(
            ImportAccountFileAsync,
            () => IsSettingsVisible && !IsBusy);
        _importBrandIconsCommand = new MobileAsyncCommand(
            ImportBrandIconsAsync,
            () => IsSettingsVisible && !IsBusy && _brandIconPackService is not null);
        _resetBrandIconsCommand = new MobileAsyncCommand(
            ResetBrandIconsAsync,
            () => IsSettingsVisible && !IsBusy && _brandIconPackService?.Status.IsInstalled == true);
        _confirmImportCommand = new MobileAsyncCommand(
            () => ResolveImportConfirmationAsync(true),
            () => IsImportConfirmationVisible);
        _cancelImportCommand = new MobileAsyncCommand(
            () => ResolveImportConfirmationAsync(false),
            () => IsImportConfirmationVisible);
        _skipAllBackupConflictsCommand = new MobileAsyncCommand(
            () => SelectAllBackupConflictsAsync(AccountImportConflictAction.Skip),
            () => IsBackupConflictResolutionVisible);
        _replaceAllBackupConflictsCommand = new MobileAsyncCommand(
            () => SelectAllBackupConflictsAsync(AccountImportConflictAction.Replace),
            () => IsBackupConflictResolutionVisible);
        _confirmBackupConflictResolutionCommand = new MobileAsyncCommand(
            ConfirmBackupConflictResolutionAsync,
            () => IsBackupConflictResolutionVisible
                && BackupImportConflicts.Any(value => value.IsReplaceSelected));
        _cancelBackupConflictResolutionCommand = new MobileAsyncCommand(
            CancelBackupConflictResolutionAsync,
            () => IsBackupConflictResolutionVisible);
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
        _selectSystemThemeCommand = new MobileAsyncCommand(
            () => SelectThemeAsync(AppThemePreference.System),
            () => IsSettingsVisible && !IsBusy && _appearanceSettingsService is not null);
        _selectDarkThemeCommand = new MobileAsyncCommand(
            () => SelectThemeAsync(AppThemePreference.Dark),
            () => IsSettingsVisible && !IsBusy && _appearanceSettingsService is not null);
        _selectLightThemeCommand = new MobileAsyncCommand(
            () => SelectThemeAsync(AppThemePreference.Light),
            () => IsSettingsVisible && !IsBusy && _appearanceSettingsService is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MobileAccountItem> Accounts => _accounts;
    public ObservableCollection<MobileAccountGroupItem> Groups => _groups;
    public ObservableCollection<MobileGroupAccountSelection> GroupEditorAccounts =>
        _groupEditorAccounts;

    public ObservableCollection<BackupImportConflictItem> BackupImportConflicts { get; } = [];

    public ICommand InitializeCommand => _initializeCommand;
    public ICommand ConfigureCommand => _configureCommand;
    public ICommand UnlockCommand => _unlockCommand;
    public ICommand BiometricUnlockCommand => _biometricUnlockCommand;
    public ICommand BeginBiometricEnrollmentCommand => _beginBiometricEnrollmentCommand;
    public ICommand BeginDeviceCredentialEnrollmentCommand =>
        _beginDeviceCredentialEnrollmentCommand;
    public ICommand SelectPasswordUnlockCommand => _selectPasswordUnlockCommand;
    public ICommand EnableBiometricCommand => _enableBiometricCommand;
    public ICommand CancelBiometricEnrollmentCommand => _cancelBiometricEnrollmentCommand;
    public ICommand ToggleAppLockCommand => _toggleAppLockCommand;
    public ICommand BeginDisableAppLockCommand => _beginDisableAppLockCommand;
    public ICommand ConfirmDisableAppLockCommand => _confirmDisableAppLockCommand;
    public ICommand CancelDisableAppLockCommand => _cancelDisableAppLockCommand;
    public ICommand EnableAppLockCommand => _enableAppLockCommand;
    public ICommand ChangeMasterPasswordCommand => _changeMasterPasswordCommand;
    public ICommand LockCommand => _lockCommand;
    public ICommand ShowAccountsCommand => _showAccountsCommand;
    public ICommand ShowSettingsCommand => _showSettingsCommand;
    public ICommand ShowSettingsCategoriesCommand => _showSettingsCategoriesCommand;
    public ICommand ShowAppearanceSettingsCommand => _showAppearanceSettingsCommand;
    public ICommand ShowBrandIconSettingsCommand => _showBrandIconSettingsCommand;
    public ICommand ShowSecuritySettingsCommand => _showSecuritySettingsCommand;
    public ICommand ShowBackupSettingsCommand => _showBackupSettingsCommand;
    public ICommand ShowImportExportSettingsCommand => _showImportExportSettingsCommand;
    public ICommand ShowMiscSettingsCommand => _showMiscSettingsCommand;
    public ICommand ShowFaqSettingsCommand => _showFaqSettingsCommand;
    public ICommand ShowImportFormatsFaqCommand => _showImportFormatsFaqCommand;
    public ICommand ClearSearchCommand => _clearSearchCommand;
    public ICommand ClearGroupEditorSearchCommand => _clearGroupEditorSearchCommand;
    public ICommand ToggleFavoritesFilterCommand => _toggleFavoritesFilterCommand;
    public ICommand ClearGroupFilterCommand => _clearGroupFilterCommand;
    public ICommand BeginAddGroupCommand => _beginAddGroupCommand;
    public ICommand SaveGroupCommand => _saveGroupCommand;
    public ICommand CancelGroupEditCommand => _cancelGroupEditCommand;
    public ICommand BeginDeleteGroupCommand => _beginDeleteGroupCommand;
    public ICommand ConfirmDeleteGroupCommand => _confirmDeleteGroupCommand;
    public ICommand CancelDeleteGroupCommand => _cancelDeleteGroupCommand;
    public ICommand BeginAddCommand => _beginAddCommand;
    public ICommand SaveAccountCommand => _saveAccountCommand;
    public ICommand CancelEditCommand => _cancelEditCommand;
    public ICommand SaveAccountAndNavigateBackCommand => _saveAccountAndNavigateBackCommand;
    public ICommand DiscardAccountChangesCommand => _discardAccountChangesCommand;
    public ICommand CancelAccountNavigationCommand => _cancelAccountNavigationCommand;
    public ICommand ClearEditorPeriodCommand => _clearEditorPeriodCommand;
    public ICommand ImportCustomIconCommand => _importCustomIconCommand;
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
    public ICommand ImportAccountFileCommand => _importAccountFileCommand;
    public ICommand ImportBrandIconsCommand => _importBrandIconsCommand;
    public ICommand ResetBrandIconsCommand => _resetBrandIconsCommand;
    public bool HasImportedBrandIcons => _brandIconPackService?.Status.IsInstalled == true;
    public string BrandIconPackStatusText
    {
        get
        {
            var status = _brandIconPackService?.Status;
            if (status?.IsInstalled != true) return string.Empty;
            var installedPacks = status.InstalledPacks ?? [];
            if (installedPacks.Count > 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Get(MobileStringKeys.BrandIconPacksStatus),
                    status.BrandCount,
                    installedPacks.Count,
                    string.Join(", ", installedPacks.Select(value => value.ProviderDisplayName)));
            }
            var providerName = string.IsNullOrWhiteSpace(status.ProviderDisplayName)
                ? "Simple Icons"
                : status.ProviderDisplayName;
            return status.Format == BrandIconPackFormat.FilenameIndexed
                || string.Equals(status.Version, "filename-indexed", StringComparison.OrdinalIgnoreCase)
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        Get(MobileStringKeys.FilenameIndexedBrandIconPackStatus),
                        status.BrandCount,
                        providerName)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        Get(MobileStringKeys.BrandIconPackStatus),
                        status.BrandCount,
                        providerName,
                        status.Version ?? string.Empty);
        }
    }
    public bool ShowIssuerLogo
    {
        get => _showIssuerLogo;
        set
        {
            if (_showIssuerLogo == value) return;
            var previous = _showIssuerLogo;
            _showIssuerLogo = value;
            var revision = ++_showIssuerLogoRevision;
            UpdateLogoVisibility();
            _ = SaveShowIssuerLogoAsync(value, previous, revision);
        }
    }
    public ICommand ConfirmImportCommand => _confirmImportCommand;
    public ICommand CancelImportCommand => _cancelImportCommand;
    public ICommand SkipAllBackupConflictsCommand => _skipAllBackupConflictsCommand;
    public ICommand ReplaceAllBackupConflictsCommand => _replaceAllBackupConflictsCommand;
    public ICommand ConfirmBackupConflictResolutionCommand =>
        _confirmBackupConflictResolutionCommand;
    public ICommand CancelBackupConflictResolutionCommand =>
        _cancelBackupConflictResolutionCommand;
    public ICommand SelectEnglishLanguageCommand => _selectEnglishLanguageCommand;
    public ICommand SelectGermanLanguageCommand => _selectGermanLanguageCommand;
    public ICommand SelectFrenchLanguageCommand => _selectFrenchLanguageCommand;
    public ICommand SelectSpanishLanguageCommand => _selectSpanishLanguageCommand;
    public ICommand SelectSystemThemeCommand => _selectSystemThemeCommand;
    public ICommand SelectDarkThemeCommand => _selectDarkThemeCommand;
    public ICommand SelectLightThemeCommand => _selectLightThemeCommand;

    public bool IsStartingVisible => _screen == MobileScreen.Starting;
    public bool IsSetupVisible => _screen == MobileScreen.Setup;
    public bool IsUnlockVisible => _screen == MobileScreen.Unlock;
    public bool IsAccountsVisible => _screen == MobileScreen.Accounts;
    public bool IsAccountListVisible => IsAccountsVisible
        && !IsSettingsVisible
        && !IsEditorVisible
        && !IsGroupEditorVisible;
    public bool CanHandleSystemBack => IsSettingsVisible || IsEditorVisible;
    public bool IsNativeAccountListVisible => IsAccountListVisible
        && HasAccounts
        && !IsImportProgressVisible
        && !IsImportConfirmationVisible
        && !IsBackupConflictResolutionVisible
        && !IsDeleteConfirmationVisible;
    public bool IsNativeAccountGroupsVisible =>
        IsNativeAccountListVisible && HasAccountNavigationCards;
    public bool IsScreenCaptureProtectionRequired => IsAccountListVisible || IsGroupEditorVisible;
    public bool IsSettingsVisible => IsAccountsVisible && _isSettingsVisible;
    public bool IsSettingsCategoryListVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.None;
    public bool IsSettingsCategoryDetailVisible =>
        IsSettingsVisible && _settingsCategory != MobileSettingsCategory.None;
    public bool IsAppearanceSettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.Appearance;
    public bool IsBrandIconSettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.BrandIcons;
    public bool IsSecuritySettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.Security;
    public bool IsBackupSettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.Backups;
    public bool IsImportExportSettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.ImportExport;
    public bool IsMiscSettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.Miscellaneous;
    public bool IsFaqSettingsVisible =>
        IsSettingsVisible && _settingsCategory == MobileSettingsCategory.Faq;
    public string SettingsCategoryTitle => _settingsCategory switch
    {
        MobileSettingsCategory.Appearance => AppearanceText,
        MobileSettingsCategory.BrandIcons => BrandIconsText,
        MobileSettingsCategory.Security => SecurityText,
        MobileSettingsCategory.Backups => BackupTitle,
        MobileSettingsCategory.ImportExport => ImportExportText,
        MobileSettingsCategory.Miscellaneous => MiscellaneousText,
        MobileSettingsCategory.Faq => FaqText,
        _ => SettingsText
    };
    public bool HasAccounts => Accounts.Count > 0;
    public bool HasNoAccounts => _hasLoadedAccounts && _allAccounts.Count == 0;
    public bool HasNoSearchResults => _allAccounts.Count > 0 && Accounts.Count == 0;
    public bool HasSearchText => SearchText.Length > 0;
    public bool HasGroupEditorSearchText => GroupEditorSearchText.Length > 0;
    public int FavoriteCount => _favoriteCount;
    public bool HasFavoriteAccounts => FavoriteCount > 0;
    public bool IsFavoritesFilterSelected => _showFavoritesOnly;
    public bool HasGroups => Groups.Count > 0;
    public bool HasAccountNavigationCards => HasFavoriteAccounts || HasGroups;
    public bool HasSelectedGroup => _selectedGroupId.HasValue;
    public bool HasActiveAccountFilter =>
        HasSearchText || IsFavoritesFilterSelected || HasSelectedGroup;
    public string SearchResultSummary => string.Format(
        Get(MobileStringKeys.SearchResultsFormat),
        Accounts.Count,
        _allAccounts.Count);
    public IImage? QrImage => _qrImage?.Image;
    public bool HasQrImage => QrImage is not null;
    public bool CanRetry => _startupFailed && !IsBusy;
    public bool IsBiometricUnlockVisible => IsUnlockVisible
        && IsSelectedPlatformUnlockAvailable
        && (IsBiometricEnabled || IsDeviceCredentialEnabled);
    public bool IsFingerprintUnlockVisible =>
        IsBiometricUnlockVisible && IsBiometricUnlockSelected;
    public bool IsDeviceCredentialUnlockVisible =>
        IsBiometricUnlockVisible && IsDeviceCredentialUnlockSelected;
    public bool IsBiometricSetupAvailable =>
        IsSettingsVisible && IsAppLockEnabled && IsBiometricAvailable && !IsBiometricEnabled;
    public bool IsBiometricUnavailable => IsSettingsVisible && !IsBiometricAvailable;
    public bool IsDeviceCredentialUnavailable =>
        IsSettingsVisible && !IsDeviceCredentialAvailable;
    public bool IsPasswordUnlockSelected =>
        DisplayedUnlockMethod == PreferredUnlockMethod.Password;
    public bool IsBiometricUnlockSelected =>
        DisplayedUnlockMethod == PreferredUnlockMethod.PlatformQuickUnlock;
    public bool IsDeviceCredentialUnlockSelected =>
        DisplayedUnlockMethod == PreferredUnlockMethod.PlatformDeviceCredential;
    public bool IsBiometricUnlockOptionEnabled =>
        IsBiometricAvailable || IsBiometricUnlockSelected;
    public bool IsDeviceCredentialUnlockOptionEnabled =>
        IsDeviceCredentialAvailable || IsDeviceCredentialUnlockSelected;
    public bool IsDeviceCredentialEnabled => IsAppLockEnabled
        && IsDeviceCredentialUnlockSelected
        && _authorization.State.ConfiguredGate == AuthorizationGateKind.DeviceCredential;
    public bool IsSelectedPlatformUnlockAvailable =>
        IsBiometricUnlockSelected && IsBiometricAvailable
        || IsDeviceCredentialUnlockSelected && IsDeviceCredentialAvailable;
    public bool IsUnlockMethodSelectionEnabled => IsSettingsVisible && IsAppLockEnabled;
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
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
            NotifyCommands();
        }
    }
    public bool IsImportConfirmationAcknowledgementOnly =>
        _isImportConfirmationAcknowledgementOnly;
    public bool IsImportConfirmationCancelVisible =>
        !_isImportConfirmationAcknowledgementOnly;
    public bool IsImportProgressVisible
    {
        get => _isImportProgressVisible;
        private set
        {
            if (!SetField(ref _isImportProgressVisible, value)) return;
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
        }
    }
    public string ImportProgressText
    {
        get => _importProgressText;
        private set => SetField(ref _importProgressText, value);
    }
    public bool IsBackupConflictResolutionVisible
    {
        get => _isBackupConflictResolutionVisible;
        private set
        {
            if (!SetField(ref _isBackupConflictResolutionVisible, value)) return;
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
            NotifyCommands();
        }
    }
    public bool IsAllBackupConflictsSkipped =>
        BackupImportConflicts.Count > 0
        && BackupImportConflicts.All(value => value.IsSkipSelected);
    public bool IsAllBackupConflictsReplaced =>
        BackupImportConflicts.Count > 0
        && BackupImportConflicts.All(value => value.IsReplaceSelected);
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
    public IReadOnlyList<MobileLanguageOption> Languages =>
    [
        new("en", EnglishLanguageText),
        new("de", GermanLanguageText),
        new("fr", FrenchLanguageText),
        new("es", SpanishLanguageText)
    ];
    public IReadOnlyList<AppLogLevel> LogLevels { get; } = Enum.GetValues<AppLogLevel>();
    public AppLogLevel MinimumLogLevel
    {
        get => _settings.Current.MinimumLogLevel;
        set
        {
            if (value == MinimumLogLevel) return;
            if (IsBusy)
            {
                OnPropertyChanged();
                return;
            }

            _ = SelectMinimumLogLevelAsync(value);
        }
    }
    public MobileLanguageOption SelectedLanguage
    {
        get => Languages.First(option => string.Equals(
            option.CultureName,
            _strings.Culture.TwoLetterISOLanguageName,
            StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null) return;
            _ = SelectLanguageAsync(value.CultureName);
        }
    }
    public bool IsSystemThemeSelected =>
        _appearanceSettingsService?.ThemePreference == AppThemePreference.System;
    public bool IsDarkThemeSelected =>
        _appearanceSettingsService?.ThemePreference == AppThemePreference.Dark;
    public bool IsLightThemeSelected =>
        _appearanceSettingsService?.ThemePreference == AppThemePreference.Light;

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
            OnPropertyChanged(nameof(IsBiometricUnlockOptionEnabled));
            OnPropertyChanged(nameof(IsSelectedPlatformUnlockAvailable));
            NotifyPlatformUnlockVisibilityChanged();
            NotifyCommands();
        }
    }

    public bool IsBiometricEnabled
    {
        get => _isBiometricEnabled;
        private set
        {
            if (!SetField(ref _isBiometricEnabled, value)) return;
            NotifyPlatformUnlockVisibilityChanged();
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
            if (_searchText.Length > 0 && _selectedGroupId.HasValue)
            {
                _selectedGroupId = null;
                RefreshGroups();
                OnPropertyChanged(nameof(HasSelectedGroup));
                _clearGroupFilterCommand.NotifyCanExecuteChanged();
            }
            OnPropertyChanged(nameof(HasSearchText));
            OnPropertyChanged(nameof(HasActiveAccountFilter));
            _clearSearchCommand.NotifyCanExecuteChanged();
            ApplyAccountFilter();
            if (_searchText.Trim().Length > 0 && Accounts.Count > 0)
                RequestAccountReveal(
                    Accounts[0].Id,
                    highlight: false,
                    alignToTop: true);
        }
    }

    public string CurrentMasterPassword
    {
        get => _currentMasterPassword;
        set
        {
            if (!SetField(ref _currentMasterPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string NewMasterPassword
    {
        get => _newMasterPassword;
        set
        {
            if (!SetField(ref _newMasterPassword, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public string NewMasterPasswordConfirmation
    {
        get => _newMasterPasswordConfirmation;
        set
        {
            if (!SetField(ref _newMasterPasswordConfirmation, value ?? string.Empty)) return;
            ClearErrorNotification();
        }
    }

    public Task ClearSearchAsync()
    {
        if (HasSearchText) SearchText = string.Empty;
        return Task.CompletedTask;
    }

    public Task ClearGroupEditorSearchAsync()
    {
        if (HasGroupEditorSearchText) GroupEditorSearchText = string.Empty;
        return Task.CompletedTask;
    }

    public Task ToggleFavoritesFilterAsync()
    {
        if (!HasFavoriteAccounts) return Task.CompletedTask;
        _showFavoritesOnly = !_showFavoritesOnly;
        _selectedGroupId = null;
        RefreshGroups();
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyAccountFilter();
        return Task.CompletedTask;
    }

    public Task ClearGroupFilterAsync()
    {
        if (!_selectedGroupId.HasValue) return Task.CompletedTask;
        _selectedGroupId = null;
        RefreshGroups();
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyAccountFilter();
        return Task.CompletedTask;
    }

#if DEBUG
    private const string DebugSyntheticAccountMarker = "otp-harbor-debug-load-test:";

    public async Task<bool> AddDebugSyntheticAccountsAsync(int count = 600)
    {
        if (!_authorization.State.IsUnlocked || IsBusy || count is < 1 or > 5000)
            return false;

        IsBusy = true;
        CancelCodeRefresh();
        var completed = false;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (loaded.IsFailed) return false;

            var accounts = loaded.Value
                .Where(account => !IsDebugSyntheticAccount(account))
                .ToList();
            var groups = new[]
            {
                new AccountGroup(new Guid("10000000-0000-0000-0000-000000000001"), "Load Test 1", "#4C956C"),
                new AccountGroup(new Guid("10000000-0000-0000-0000-000000000002"), "Load Test 2", "#18A999"),
                new AccountGroup(new Guid("10000000-0000-0000-0000-000000000003"), "Load Test 3", "#4F6BED"),
                new AccountGroup(new Guid("10000000-0000-0000-0000-000000000004"), "Load Test 4", "#F59E0B"),
                new AccountGroup(new Guid("10000000-0000-0000-0000-000000000005"), "Load Test 5", "#E45757"),
                new AccountGroup(new Guid("10000000-0000-0000-0000-000000000006"), "Load Test 6", "#B455C7")
            };
            for (var index = 1; index <= count; index++)
            {
                accounts.Add(new Account(
                    Guid.NewGuid(),
                    $"Debug Service {index:0000}",
                    "JBSWY3DPEHPK3PXP",
                    $"{DebugSyntheticAccountMarker}{index:0000}",
                    index % 10 == 0 ? 60 : TotpPeriodPolicy.DefaultSeconds,
                    groups[(index - 1) % groups.Length],
                    isFavorite: index % 8 == 0));
            }

            var saved = await _accountManager.CommitImportAsync(accounts);
            if (saved.IsFailed) return false;
            await LoadAccountsAsync();
            completed = true;
            return true;
        }
        finally
        {
            IsBusy = false;
            if (!completed) StartCodeRefresh();
        }
    }

    public async Task<bool> DeleteDebugSyntheticAccountsAsync()
    {
        if (!_authorization.State.IsUnlocked || IsBusy) return false;

        IsBusy = true;
        CancelCodeRefresh();
        var completed = false;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (loaded.IsFailed) return false;
            var retained = loaded.Value
                .Where(account => !IsDebugSyntheticAccount(account))
                .ToArray();
            if (retained.Length == loaded.Value.Count)
            {
                completed = true;
                StartCodeRefresh();
                return true;
            }

            var saved = await _accountManager.CommitImportAsync(retained);
            if (saved.IsFailed) return false;
            _showFavoritesOnly = false;
            _selectedGroupId = null;
            await LoadAccountsAsync();
            completed = true;
            return true;
        }
        finally
        {
            IsBusy = false;
            if (!completed) StartCodeRefresh();
        }
    }

    public async Task<bool> ImportAccountsAsync(
        Stream jsonStream,
        string fileName = "otp-harbor-load-test-500.json",
        CancellationToken cancellationToken = default)
    {
        if (!_authorization.State.IsUnlocked
            || IsBusy
            || !Path.GetExtension(fileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(jsonStream);
        IsBusy = true;
        CancelCodeRefresh();
        BeginImportProgress(MobileStringKeys.ImportingAccounts);
        var completed = false;
        try
        {
            var decoded = await _exportService.ImportFromStreamAsync(
                jsonStream,
                fileName,
                cancellationToken: cancellationToken);
            if (decoded.IsFailed || decoded.Value.Count == 0) return false;
            var imported = await _accountImport.ImportAsync(
                decoded.Value,
                ImportConflictStrategy.SkipExisting,
                (_, _) => Task.FromResult(true),
                cancellationToken);
            if (imported.IsFailed
                || imported.Value.Status != AccountImportStatus.Completed)
            {
                return false;
            }

            await LoadAccountsAsync();
            completed = true;
            return true;
        }
        finally
        {
            EndImportProgress();
            IsBusy = false;
            if (!completed) StartCodeRefresh();
        }
    }

    public async Task<bool> DeleteAllAccountsAsync()
    {
        if (!_authorization.State.IsUnlocked || IsBusy) return false;

        IsBusy = true;
        CancelCodeRefresh();
        var completed = false;
        try
        {
            var saved = await _accountManager.CommitImportAsync([]);
            if (saved.IsFailed) return false;
            _showFavoritesOnly = false;
            _selectedGroupId = null;
            await LoadAccountsAsync();
            completed = true;
            return true;
        }
        finally
        {
            IsBusy = false;
            if (!completed) StartCodeRefresh();
        }
    }

    private static bool IsDebugSyntheticAccount(Account account) =>
        account.AccountName?.StartsWith(
            DebugSyntheticAccountMarker,
            StringComparison.Ordinal) == true;
#endif

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

    public MobileAccountRevealRequest? AccountRevealRequest
    {
        get => _accountRevealRequest;
        private set => SetField(ref _accountRevealRequest, value);
    }

    public MobileAccountGroupRevealRequest? GroupRevealRequest
    {
        get => _groupRevealRequest;
        private set => SetField(ref _groupRevealRequest, value);
    }

    public bool IsGroupEditorVisible
    {
        get => _isGroupEditorVisible;
        private set
        {
            if (!SetField(ref _isGroupEditorVisible, value)) return;
            OnPropertyChanged(nameof(IsAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
            OnPropertyChanged(nameof(IsScreenCaptureProtectionRequired));
            OnPropertyChanged(nameof(GroupEditorTitle));
            OnPropertyChanged(nameof(IsCreatingGroup));
            OnPropertyChanged(nameof(IsEditingGroup));
            NotifyCommands();
        }
    }

    public bool IsCreatingGroup => IsGroupEditorVisible && !_editingGroupId.HasValue;
    public bool IsEditingGroup => IsGroupEditorVisible && _editingGroupId.HasValue;

    public bool IsDeleteGroupConfirmationVisible
    {
        get => _isDeleteGroupConfirmationVisible;
        private set
        {
            if (!SetField(ref _isDeleteGroupConfirmationVisible, value)) return;
            OnPropertyChanged(nameof(GroupDeletePrompt));
            NotifyCommands();
        }
    }

    public string GroupEditorName
    {
        get => _groupEditorName;
        set
        {
            if (!SetField(ref _groupEditorName, value ?? string.Empty)) return;
            GroupEditorMessage = string.Empty;
            ClearErrorNotification();
        }
    }

    public string GroupEditorSearchText
    {
        get => _groupEditorSearchText;
        set
        {
            if (!SetField(ref _groupEditorSearchText, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(HasGroupEditorSearchText));
            _clearGroupEditorSearchCommand.NotifyCanExecuteChanged();
            ApplyGroupEditorSearch();
        }
    }

    public string GroupEditorMessage
    {
        get => _groupEditorMessage;
        private set => SetField(ref _groupEditorMessage, value);
    }

    public IReadOnlyList<MobileGroupColorOption> GroupColorOptions
    {
        get => _groupColorOptions;
        private set => SetField(ref _groupColorOptions, value);
    }

    public MobileGroupColorOption? SelectedGroupColor
    {
        get => _selectedGroupColor;
        set => SetField(ref _selectedGroupColor, value);
    }

    public string GroupEditorTitle => Get(IsEditingGroup
        ? MobileStringKeys.EditGroup
        : MobileStringKeys.CreateGroup);

    public string GroupDeletePrompt => string.Format(
        Get(MobileStringKeys.DeleteGroupPrompt),
        GroupEditorName);

    public bool IsEditorVisible
    {
        get => _isEditorVisible;
        private set
        {
            if (!SetField(ref _isEditorVisible, value)) return;
            OnPropertyChanged(nameof(IsAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
            OnPropertyChanged(nameof(IsScreenCaptureProtectionRequired));
            OnPropertyChanged(nameof(CanHandleSystemBack));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(EditorSecretPlaceholder));
            NotifyCommands();
        }
    }

    public bool IsAccountEditorExitConfirmationVisible
    {
        get => _isAccountEditorExitConfirmationVisible;
        private set
        {
            if (!SetField(ref _isAccountEditorExitConfirmationVisible, value)) return;
            NotifyCommands();
        }
    }

    public bool HasUnsavedAccountChanges =>
        IsEditorVisible
        && _accountEditorBaseline is not null
        && _accountEditorBaseline != CaptureAccountEditorSnapshot();

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set
        {
            if (!SetField(ref _isDeleteConfirmationVisible, value)) return;
            OnPropertyChanged(nameof(IsNativeAccountListVisible));
            OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
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
            EditorIssuerMessage = string.Empty;
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
            EditorSecretMessage = string.Empty;
            ClearErrorNotification();
        }
    }

    public int? EditorPeriodSeconds
    {
        get => _editorPeriodSeconds;
        set
        {
            if (!SetField(ref _editorPeriodSeconds, value)) return;
            OnPropertyChanged(nameof(HasEditorPeriodSeconds));
            _clearEditorPeriodCommand.NotifyCanExecuteChanged();
            EditorPeriodMessage = string.Empty;
            ClearErrorNotification();
        }
    }

    public bool HasEditorPeriodSeconds => EditorPeriodSeconds.HasValue;

    public bool EditorIsFavorite
    {
        get => _editorIsFavorite;
        set => SetField(ref _editorIsFavorite, value);
    }

    public IReadOnlyList<MobileBrandIconOption> EditorBrandIconOptions
    {
        get => _editorBrandIconOptions;
        private set
        {
            if (!SetField(ref _editorBrandIconOptions, value)) return;
            OnPropertyChanged(nameof(HasBrandIconChoices));
            OnPropertyChanged(nameof(EditorBrandIconPickerWidth));
        }
    }

    public MobileBrandIconOption? SelectedEditorBrandIconOption
    {
        get => _selectedEditorBrandIconOption;
        set
        {
            if (!SetField(ref _selectedEditorBrandIconOption, value)) return;
            OnPropertyChanged(nameof(SelectedEditorBrandIconFileName));
            OnPropertyChanged(nameof(HasSelectedEditorBrandIconFileName));
            OnPropertyChanged(nameof(SelectedEditorCustomIconFileName));
            OnPropertyChanged(nameof(HasSelectedEditorCustomIconFileName));
        }
    }

    public bool HasBrandIconChoices => EditorBrandIconOptions.Count > 1;
    public double EditorBrandIconPickerWidth =>
        Math.Clamp(
            (EditorBrandIconOptions.Count == 0
                ? Get(MobileStringKeys.AutomaticBrandIcon).Length
                : EditorBrandIconOptions.Max(option => option.DisplayName.Length)) * 8d + 32d,
            180d,
            210d);
    public string SelectedEditorBrandIconFileName =>
        SelectedEditorBrandIconOption?.FileName ?? string.Empty;
    public bool HasSelectedEditorBrandIconFileName =>
        !string.IsNullOrWhiteSpace(SelectedEditorBrandIconFileName);
    public bool HasSelectedEditorCustomIconFileName =>
        SelectedEditorBrandIconOption?.Id?.StartsWith("custom_", StringComparison.Ordinal) == true
        && HasSelectedEditorBrandIconFileName;
    public string SelectedEditorCustomIconFileName =>
        HasSelectedEditorCustomIconFileName
            ? string.Format(
                CultureInfo.CurrentCulture,
                Get(MobileStringKeys.CustomIconFileName),
                Shared.Presentation.IconNameDisplayPolicy.Truncate(
                    SelectedEditorBrandIconFileName))
            : string.Empty;

    public Task ClearEditorPeriodAsync()
    {
        EditorPeriodSeconds = null;
        return Task.CompletedTask;
    }

    public string EditorIssuerMessage
    {
        get => _editorIssuerMessage;
        private set => SetField(ref _editorIssuerMessage, value);
    }

    public string EditorSecretMessage
    {
        get => _editorSecretMessage;
        private set => SetField(ref _editorSecretMessage, value);
    }

    public string EditorPeriodMessage
    {
        get => _editorPeriodMessage;
        private set => SetField(ref _editorPeriodMessage, value);
    }

    public bool IsAdvancedOptionsExpanded
    {
        get => _isAdvancedOptionsExpanded;
        set => SetField(ref _isAdvancedOptionsExpanded, value);
    }

    public string EditorTitle => Get(_editingAccountId.HasValue
        ? MobileStringKeys.EditorEditTitle
        : MobileStringKeys.EditorAddTitle);

    public bool IsEditingExistingAccount => _editingAccountId.HasValue;

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
    public string ChangeMasterPasswordText => Get(MobileStringKeys.ChangeMasterPassword);
    public string ChangeMasterPasswordDescriptionText =>
        Get(MobileStringKeys.ChangeMasterPasswordDescription);
    public string CurrentPasswordText => Get(MobileStringKeys.CurrentPassword);
    public string NewPasswordText => Get(MobileStringKeys.NewPassword);
    public string RevealPasswordText => Get(MobileStringKeys.RevealPassword);
    public string RevealPasswordHelpText => Get(MobileStringKeys.RevealPasswordHelp);
    public string CreateVaultText => Get(MobileStringKeys.CreateVault);
    public string UnlockTitle => Get(MobileStringKeys.UnlockTitle);
    public string UnlockDescription => Get(MobileStringKeys.UnlockDescription);
    public string UnlockText => Get(MobileStringKeys.Unlock);
    public string AccountsTitle => Get(MobileStringKeys.AccountsTitle);
    public string NoAccountsText => Get(MobileStringKeys.NoAccounts);
    public string SetUpFirstAccountText => Get(MobileStringKeys.SetUpFirstAccount);
    public string AddAccountText => Get(MobileStringKeys.AddAccount);
    public string EditAccountText => Get(MobileStringKeys.EditAccount);
    public string DeleteAccountText => Get(MobileStringKeys.DeleteAccount);
    public string LockText => Get(MobileStringKeys.Lock);
    public string IssuerText => Get(MobileStringKeys.Issuer);
    public string AccountNameText => Get(MobileStringKeys.AccountName);
    public string AdvancedOptionsText => Get(MobileStringKeys.AdvancedOptions);
    public string TotpPeriodText => Get(MobileStringKeys.TotpPeriod);
    public string ClearPeriodText => Get(MobileStringKeys.ClearPeriod);
    public string TotpPeriodHelpText => Get(MobileStringKeys.TotpPeriodHelp);
    public string SaveText => Get(MobileStringKeys.Save);
    public string CancelText => Get(MobileStringKeys.Cancel);
    public string CopyCodeText => Get(MobileStringKeys.CopyCode);
    public string DeleteConfirmTitle => Get(MobileStringKeys.DeleteConfirmTitle);
    public string DeleteText => Get(MobileStringKeys.Delete);
    public string BiometricUnlockText => IsDeviceCredentialUnlockSelected
        ? Get(MobileStringKeys.UnlockWithDevicePin)
        : Get(MobileStringKeys.BiometricUnlock);
    public string BiometricSetupTitle => Get(MobileStringKeys.BiometricSetupTitle);
    public string BiometricSetupDescription => Get(MobileStringKeys.BiometricSetupDescription);
    public string BiometricEnableText => Get(MobileStringKeys.BiometricEnable);
    public string BiometricEnabledText => Get(MobileStringKeys.BiometricEnabled);
    public string BiometricUnavailableText => Get(MobileStringKeys.BiometricUnavailable);
    public string UnlockMethodTitleText => Get(MobileStringKeys.UnlockMethodTitle);
    public string UnlockMethodDescriptionText => Get(MobileStringKeys.UnlockMethodDescription);
    public string UnlockWithPasswordText => Get(MobileStringKeys.UnlockWithPassword);
    public string UnlockWithBiometricsText => Get(MobileStringKeys.UnlockWithBiometrics);
    public string UnlockWithDevicePinText => Get(MobileStringKeys.UnlockWithDevicePin);
    public string DeviceCredentialUnlockButtonText =>
        Get(MobileStringKeys.DeviceCredentialUnlockButton);
    public string DevicePinUnavailableText => Get(MobileStringKeys.DevicePinUnavailable);
    public string UnlockMethodPasswordPromptText =>
        Get(MobileStringKeys.UnlockMethodPasswordPrompt);
    public string ApplyUnlockMethodText => Get(MobileStringKeys.ApplyUnlockMethod);
    public string CodesText => Get(MobileStringKeys.Codes);
    public string SettingsText => Get(MobileStringKeys.Settings);
    public string CloseSettingsText => Get(MobileStringKeys.CloseSettings);
    public string BackToSettingsText => Get(MobileStringKeys.BackToSettings);
    public string BackToTopText => Get(MobileStringKeys.BackToTop);
    public string AppearanceSettingsDescriptionText =>
        Get(MobileStringKeys.AppearanceSettingsDescription);
    public string BrandIconSettingsDescriptionText =>
        Get(MobileStringKeys.BrandIconSettingsDescription);
    public string SecuritySettingsDescriptionText =>
        Get(MobileStringKeys.SecuritySettingsDescription);
    public string BackupSettingsDescriptionText =>
        Get(MobileStringKeys.BackupSettingsDescription);
    public string ImportExportSettingsDescriptionText =>
        Get(MobileStringKeys.ImportExportSettingsDescription);
    public string MiscellaneousText => Get(MobileStringKeys.Miscellaneous);
    public string MiscSettingsDescriptionText => Get(MobileStringKeys.MiscSettingsDescription);
    public string LoggingLevelText => Get(MobileStringKeys.LoggingLevel);
    public string FaqText => Get(MobileStringKeys.Faq);
    public string FaqSettingsDescriptionText => Get(MobileStringKeys.FaqSettingsDescription);
    public string FaqImportIconPacksQuestionText =>
        Get(MobileStringKeys.FaqImportIconPacksQuestion);
    public string FaqImportIconPacksAnswerText =>
        Get(MobileStringKeys.FaqImportIconPacksAnswer);
    public string FaqImportIconPacksSourcesText => Get(MobileStringKeys.FaqImportIconPacksSources);
    public string FaqSimpleIconsOfficialLinkText => Get(MobileStringKeys.FaqSimpleIconsOfficialLink);
    public string FaqAegisIconPackDocsLinkText => Get(MobileStringKeys.FaqAegisIconPackDocsLink);
    public string FaqImportIconPacksDisclaimerText => Get(MobileStringKeys.FaqImportIconPacksDisclaimer);
    public string FaqImportFormatsQuestionText => Get(MobileStringKeys.FaqImportFormatsQuestion);
    public string FaqImportFormatsIntroText => Get(MobileStringKeys.FaqImportFormatsIntro);
    public string FaqImportFormatsAegisTitleText => Get(MobileStringKeys.FaqImportFormatsAegisTitle);
    public string FaqImportFormatsAegisDescriptionText => Get(MobileStringKeys.FaqImportFormatsAegisDescription);
    public string FaqImportFormatsAegisExampleText => Get(MobileStringKeys.FaqImportFormatsAegisExample);
    public string FaqImportFormatsTwoFasTitleText => Get(MobileStringKeys.FaqImportFormatsTwoFasTitle);
    public string FaqImportFormatsTwoFasDescriptionText => Get(MobileStringKeys.FaqImportFormatsTwoFasDescription);
    public string FaqImportFormatsTwoFasExampleText => Get(MobileStringKeys.FaqImportFormatsTwoFasExample);
    public string FaqImportFormatsOtpAuthTitleText => Get(MobileStringKeys.FaqImportFormatsOtpAuthTitle);
    public string FaqImportFormatsOtpAuthDescriptionText => Get(MobileStringKeys.FaqImportFormatsOtpAuthDescription);
    public string FaqImportFormatsOtpAuthExampleText => Get(MobileStringKeys.FaqImportFormatsOtpAuthExample);
    public string LanguageText => Get(MobileStringKeys.Language);
    public string EnglishLanguageText => Get(MobileStringKeys.EnglishLanguage);
    public string GermanLanguageText => Get(MobileStringKeys.GermanLanguage);
    public string FrenchLanguageText => Get(MobileStringKeys.FrenchLanguage);
    public string SpanishLanguageText => Get(MobileStringKeys.SpanishLanguage);
    public string AppearanceText => Get(MobileStringKeys.Appearance);
    public string ThemeFollowSystemText => Get(MobileStringKeys.ThemeFollowSystem);
    public string ThemeDarkText => Get(MobileStringKeys.ThemeDark);
    public string ThemeLightText => Get(MobileStringKeys.ThemeLight);
    public string SecurityText => Get(MobileStringKeys.Security);
    public string AppLockTitleText => Get(MobileStringKeys.AppLockTitle);
    public string AppLockEnabledDescriptionText =>
        Get(MobileStringKeys.AppLockEnabledDescription);
    public string AppLockDisabledDescriptionText =>
        Get(MobileStringKeys.AppLockDisabledDescription);
    public string DisableAppLockText => Get(MobileStringKeys.DisableAppLock);
    public string EnableAppLockText => Get(MobileStringKeys.EnableAppLock);
    public string AppLockActionText => IsAppLockEnabled
        ? DisableAppLockText
        : EnableAppLockText;
    public string DisableAppLockWarningText => Get(MobileStringKeys.DisableAppLockWarning);
    public string SearchAccountsText => Get(MobileStringKeys.SearchAccounts);
    public string SearchBrandIconsText => Get(MobileStringKeys.SearchBrandIcons);
    public string ClearSearchText => Get(MobileStringKeys.ClearSearch);
    public string FavoritesText => Get(MobileStringKeys.Favorites);
    public string FavoriteAccountText => Get(MobileStringKeys.FavoriteAccount);
    public string AddToFavoritesText => Get(MobileStringKeys.AddToFavorites);
    public string RemoveFromFavoritesText => Get(MobileStringKeys.RemoveFromFavorites);
    public string CreateGroupText => Get(MobileStringKeys.CreateGroup);
    public string EditGroupText => Get(MobileStringKeys.EditGroup);
    public string DeleteGroupText => Get(MobileStringKeys.DeleteGroup);
    public string GroupNameText => Get(MobileStringKeys.GroupName);
    public string GroupColorText => Get(MobileStringKeys.GroupColor);
    public string GroupAccountsText => Get(MobileStringKeys.GroupAccounts);
    public string NoSearchResultsText => Get(MobileStringKeys.NoSearchResults);
    public string AccountSwipeHintText => Get(MobileStringKeys.AccountSwipeHint);
    public string ScanQrText => Get(MobileStringKeys.ScanQr);
    public string ImportExportText => Get(MobileStringKeys.ImportExport);
    public string ImportSectionText => Get(MobileStringKeys.ImportSection);
    public string ExportSectionText => Get(MobileStringKeys.ExportSection);
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
    public string BackupSectionText => Get(MobileStringKeys.BackupSection);
    public string RestoreSectionText => Get(MobileStringKeys.RestoreSection);
    public string BackupDescription => Get(MobileStringKeys.BackupDescription);
    public string ImportBackupDescriptionText =>
        Get(MobileStringKeys.ImportBackupDescription);
    public string ImportAccountFileText => Get(MobileStringKeys.ImportAccountFile);
    public string ImportAccountFileDescriptionText =>
        Get(MobileStringKeys.ImportAccountFileDescription);
    public string ImportFormatAegisText => Get(MobileStringKeys.ImportFormatAegis);
    public string ImportFormatTwoFasText => Get(MobileStringKeys.ImportFormatTwoFas);
    public string ImportFormatOtpAuthText => Get(MobileStringKeys.ImportFormatOtpAuth);
    public string ViewImportFormatsFaqText => Get(MobileStringKeys.ViewImportFormatsFaq);
    public string ExportBackupDescriptionText =>
        Get(MobileStringKeys.ExportBackupDescription);
    public string BackupPasswordText => Get(MobileStringKeys.BackupPassword);
    public string ConfirmBackupPasswordText => Get(MobileStringKeys.ConfirmBackupPassword);
    public string ExportBackupText => Get(MobileStringKeys.ExportBackup);
    public string ImportBackupText => Get(MobileStringKeys.ImportBackup);
    public string BrandIconsText => Get(MobileStringKeys.BrandIcons);
    public string BrandIconsDescriptionText => Get(MobileStringKeys.BrandIconsDescription);
    public string ChooseCustomSvgIconText => Get(MobileStringKeys.ChooseCustomSvgIcon);
    public string OrText => Get(MobileStringKeys.Or);
    public string UnsavedAccountChangesTitleText =>
        Get(MobileStringKeys.UnsavedAccountChangesTitle);
    public string UnsavedAccountChangesPromptText =>
        Get(MobileStringKeys.UnsavedAccountChangesPrompt);
    public string DiscardChangesText => Get(MobileStringKeys.DiscardChanges);
    public string BrandIconText => Get(MobileStringKeys.BrandIcon);
    public string BrandIconHelpText => Get(MobileStringKeys.BrandIconHelp);
    public string ImportSimpleIconsPackText => Get(MobileStringKeys.ImportSimpleIconsPack);
    public string ShowIssuerLogoText => Get(MobileStringKeys.ShowIssuerLogo);
    public string ShowIssuerLogoDescriptionText => Get(MobileStringKeys.ShowIssuerLogoDescription);
    public string ResetBrandIconsText => Get(MobileStringKeys.ResetBrandIcons);
    public string ImportConfirmationTitle => Get(_isImportConfirmationAcknowledgementOnly
        ? MobileStringKeys.NoImportTitle
        : MobileStringKeys.ImportConfirmationTitle);
    public string ImportConfirmationText
    {
        get => _importConfirmationText;
        private set => SetField(ref _importConfirmationText, value);
    }

    public bool IsDeviceCredentialAvailable
    {
        get => _isDeviceCredentialAvailable;
        private set
        {
            if (!SetField(ref _isDeviceCredentialAvailable, value)) return;
            OnPropertyChanged(nameof(IsDeviceCredentialUnavailable));
            OnPropertyChanged(nameof(IsSelectedPlatformUnlockAvailable));
            NotifyPlatformUnlockVisibilityChanged();
            OnPropertyChanged(nameof(IsDeviceCredentialUnlockOptionEnabled));
            NotifyCommands();
        }
    }
    public string ConfirmImportText => Get(_isImportConfirmationAcknowledgementOnly
        ? MobileStringKeys.Ok
        : MobileStringKeys.ConfirmImport);
    public string BackupConflictResolutionTitle =>
        Get(MobileStringKeys.BackupConflictResolutionTitle);
    public string BackupConflictResolutionText
    {
        get => _backupConflictResolutionText;
        private set => SetField(ref _backupConflictResolutionText, value);
    }
    public string ConflictAccountText => Get(MobileStringKeys.ConflictAccount);
    public string SkipText => Get(MobileStringKeys.Skip);
    public string OverrideText => Get(MobileStringKeys.Override);
    public string SkipAllText => Get(MobileStringKeys.SkipAll);
    public string OverrideAllText => Get(MobileStringKeys.OverrideAll);
    public string ApplyToAllText => Get(MobileStringKeys.ApplyToAll);
    public string CurrentAccountFormat => Get(MobileStringKeys.CurrentAccountFormat);
    public string BackupAccountFormat => Get(MobileStringKeys.BackupAccountFormat);
    public string ChangedFieldsFormat => Get(MobileStringKeys.ChangedFieldsFormat);
    public string IssuerFieldText => Get(MobileStringKeys.IssuerField);
    public string AccountNameFieldText => Get(MobileStringKeys.AccountNameField);
    public string SecretFieldText => Get(MobileStringKeys.SecretField);
    public string PeriodFieldText => Get(MobileStringKeys.PeriodField);
    public string FavoriteFieldText => Get(MobileStringKeys.FavoriteField);
    public string KeepAccountAutomationFormat =>
        Get(MobileStringKeys.KeepAccountAutomationFormat);
    public string RestoreAccountAutomationFormat =>
        Get(MobileStringKeys.RestoreAccountAutomationFormat);

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

            // ISettingsService keeps a stable mutable Current instance. Notify bindings after
            // LoadAsync applies persisted values so the UI cannot retain constructor defaults.
            NotifyAppLockChanged();
            _strings.ApplyCulture(_settings.Current.CultureName);
            NotifyLocalizedTextChanged();
            SetNotification(Get(MobileStringKeys.Starting), NotificationSeverity.Information);

            await _authorization.InitializeAsync();
            await RefreshUnlockMethodAvailabilityAsync();
            RefreshUnlockMethodState();
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

            // Android can report biometric hardware as temporarily unavailable while the
            // activity is still starting. Re-query after password setup so that a stale
            // startup result cannot disable the secure default enrollment flow.
            await RefreshUnlockMethodAvailabilityAsync();
            var biometricEnrollment = await TryEnrollBiometricsAfterSetupAsync(password);
            SetScreen(MobileScreen.Accounts);
            await LoadAccountsAsync();
            ApplyBiometricEnrollmentOutcome(biometricEnrollment);
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
        var unlocked = false;
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
            unlocked = true;
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

        if (unlocked) await RestorePostUnlockNavigationAsync();
    }

    public async Task BiometricUnlockAsync()
    {
        if (!IsBiometricUnlockVisible || IsBusy) return;

        var unlocked = false;
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
                unlocked = true;
            }
            else if (result != AuthorizationResult.Cancelled)
            {
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
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.BiometricUnlockFailed);
        }
        finally
        {
            IsBusy = false;
        }

        if (unlocked) await RestorePostUnlockNavigationAsync();
    }

    public Task BeginBiometricEnrollmentAsync()
        => BeginPlatformUnlockEnrollmentAsync(
            PreferredUnlockMethod.PlatformQuickUnlock);

    public Task BeginDeviceCredentialEnrollmentAsync()
        => BeginPlatformUnlockEnrollmentAsync(
            PreferredUnlockMethod.PlatformDeviceCredential);

    private Task BeginPlatformUnlockEnrollmentAsync(PreferredUnlockMethod method)
    {
        if (!CanSelectUnlockMethod(method)) return Task.CompletedTask;
        if (DisplayedUnlockMethod == method) return Task.CompletedTask;
        _pendingUnlockMethod = method;
        _isReenablingAppLock = false;
        BiometricRecoveryPassword = string.Empty;
        IsBiometricEnrollmentVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task SelectPasswordUnlockAsync() =>
        BeginPlatformUnlockEnrollmentAsync(PreferredUnlockMethod.Password);

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
            var result = _isReenablingAppLock
                ? await _authorization.SetAppLockEnabledAsync(true, recoveryPassword)
                : _pendingUnlockMethod == PreferredUnlockMethod.PlatformQuickUnlock
                    ? await _authorization.ConfigureHelloAsync(recoveryPassword)
                    : await _authorization.ConfigureUnlockMethodAsync(
                        _pendingUnlockMethod,
                        recoveryPassword);
            if (result == AuthorizationResult.Cancelled) return;
            if (result != AuthorizationResult.Success)
            {
                SetError(GetUnlockMethodErrorKey(result, _pendingUnlockMethod));
                if (_isReenablingAppLock && IsAppLockEnabled)
                {
                    RefreshUnlockMethodState();
                    NotifyAppLockChanged();
                    LockCore();
                }
                return;
            }

            IsBiometricEnrollmentVisible = false;
            BiometricRecoveryPassword = string.Empty;
            RefreshUnlockMethodState();
            NotifyAppLockChanged();
            NotifyUnlockMethodChanged();
            if (_isReenablingAppLock)
                LockCore();
            else
                SetSuccess(MobileStringKeys.UnlockMethodChanged);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.UnlockMethodChangeFailed);
        }
        finally
        {
            _isReenablingAppLock = false;
            recoveryPassword = string.Empty;
            IsBusy = false;
        }
    }

    private async Task<AuthorizationResult?> TryEnrollBiometricsAfterSetupAsync(
        string recoveryPassword)
    {
        if (!IsBiometricAvailable || !_settings.Current.AppLockEnabled)
            return null;

        try
        {
            var result = await _authorization.ConfigureHelloAsync(recoveryPassword);
            if (result == AuthorizationResult.Success)
                IsBiometricEnabled = true;

            return result;
        }
        catch (Exception)
        {
            // Password setup has already succeeded. A platform enrollment failure must
            // not strand the user outside the newly created vault.
            return AuthorizationResult.Failed;
        }
    }

    private async Task RefreshUnlockMethodAvailabilityAsync()
    {
        try
        {
            IsBiometricAvailable = await _authorization.IsHelloAvailableAsync();
        }
        catch (Exception)
        {
            IsBiometricAvailable = false;
        }

        try
        {
            IsDeviceCredentialAvailable =
                await _authorization.IsUnlockMethodAvailableAsync(
                    PreferredUnlockMethod.PlatformDeviceCredential);
        }
        catch (Exception)
        {
            IsDeviceCredentialAvailable = false;
        }
    }

    private void ApplyBiometricEnrollmentOutcome(AuthorizationResult? result)
    {
        if (result == AuthorizationResult.Success)
        {
            SetSuccess(MobileStringKeys.BiometricEnabled);
            return;
        }

        if (result.HasValue && result != AuthorizationResult.Cancelled)
            SetError(GetBiometricEnrollmentErrorKey(result.Value));
    }

    private static string GetBiometricEnrollmentErrorKey(AuthorizationResult result) =>
        result switch
        {
            AuthorizationResult.InvalidCredentials => MobileStringKeys.UnlockRejected,
            AuthorizationResult.DisabledByPolicy => MobileStringKeys.BiometricDisabledByPolicy,
            AuthorizationResult.TooManyAttempts => MobileStringKeys.BiometricRetriesExhausted,
            _ => MobileStringKeys.BiometricEnableFailed
        };

    private static string GetUnlockMethodErrorKey(
        AuthorizationResult result,
        PreferredUnlockMethod method) => result switch
        {
            AuthorizationResult.InvalidCredentials => MobileStringKeys.UnlockRejected,
            AuthorizationResult.DisabledByPolicy =>
                method == PreferredUnlockMethod.PlatformDeviceCredential
                    ? MobileStringKeys.DevicePinUnavailable
                    : MobileStringKeys.BiometricDisabledByPolicy,
            AuthorizationResult.TooManyAttempts => MobileStringKeys.BiometricRetriesExhausted,
            _ => MobileStringKeys.UnlockMethodChangeFailed
        };

    public Task CancelBiometricEnrollmentAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        BiometricRecoveryPassword = string.Empty;
        IsBiometricEnrollmentVisible = false;
        _isReenablingAppLock = false;
        NotifyUnlockMethodChanged();
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

    public async Task ToggleAppLockAsync()
    {
        if (!IsSettingsVisible || IsBusy) return;

        if (IsAppLockEnabled)
            await BeginDisableAppLockAsync();
        else
            await EnableAppLockAsync();
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
            RefreshUnlockMethodState();
            NotifyAppLockChanged();
            NotifyUnlockMethodChanged();
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

        var preferred = _settings.Current.PreferredUnlockMethod;
        if (preferred is PreferredUnlockMethod.PlatformQuickUnlock
            or PreferredUnlockMethod.PlatformDeviceCredential)
        {
            _pendingUnlockMethod = preferred;
            _isReenablingAppLock = true;
            BiometricRecoveryPassword = string.Empty;
            IsBiometricEnrollmentVisible = true;
            ClearNotification();
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authorization.SetAppLockEnabledAsync(true, string.Empty);
            NotifyAppLockChanged();
            NotifyUnlockMethodChanged();
            if (result != AuthorizationResult.Success || !IsAppLockEnabled)
            {
                if (IsAppLockEnabled)
                {
                    LockCore();
                    return;
                }
                SetError(MobileStringKeys.AppLockChangeFailed);
                return;
            }

            // Enforce the newly enabled policy immediately. Remaining on the
            // authorized settings screen makes a successful toggle look
            // ineffective and leaves codes exposed until a later lifecycle
            // transition.
            LockCore();
        }
        catch (Exception)
        {
            NotifyAppLockChanged();
            if (IsAppLockEnabled)
            {
                LockCore();
                return;
            }

            SetError(MobileStringKeys.AppLockChangeFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ChangeMasterPasswordAsync()
    {
        if (!IsSecuritySettingsVisible || IsBusy) return;

        var currentPassword = CurrentMasterPassword;
        var newPassword = NewMasterPassword;
        var confirmation = NewMasterPasswordConfirmation;
        ClearMasterPasswordChangeInputs();

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            SetError(MobileStringKeys.CurrentPasswordRequired);
            return;
        }

        if (string.IsNullOrWhiteSpace(newPassword)
            || string.IsNullOrWhiteSpace(confirmation))
        {
            SetError(MobileStringKeys.PasswordRequired);
            return;
        }

        if (newPassword.Length < _passwordValidation.MinimumLength)
        {
            SetNotification(
                string.Format(
                    Get(MobileStringKeys.PasswordMinimumLength),
                    _passwordValidation.MinimumLength),
                NotificationSeverity.Error);
            return;
        }

        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
        {
            SetError(MobileStringKeys.PasswordMismatch);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authorization.ChangePasswordAsync(currentPassword, newPassword);
            if (result == AuthorizationResult.Success)
            {
                SetSuccess(MobileStringKeys.PasswordChanged);
                return;
            }

            SetError(result == AuthorizationResult.InvalidCredentials
                ? MobileStringKeys.PasswordVerificationFailed
                : MobileStringKeys.PasswordChangeFailed);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.PasswordChangeFailed);
        }
        finally
        {
            currentPassword = string.Empty;
            newPassword = string.Empty;
            confirmation = string.Empty;
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
        _settingsCategory = MobileSettingsCategory.None;
        ResetPendingUnlockMethodChange();
        IsDisableAppLockConfirmationVisible = false;
        ClearPasswordInputs();
        ClearNotification();
        NotifyUnlockedSectionChanged();
        StartCodeRefresh();
        return Task.CompletedTask;
    }

    public async Task ShowSettingsAsync()
    {
        if (!IsAccountsVisible || IsSettingsVisible || IsEditorVisible || IsBusy)
            return;

        _isSettingsVisible = true;
        _settingsCategory = MobileSettingsCategory.None;
        ResetPendingUnlockMethodChange();
        IsDeleteConfirmationVisible = false;
        IsDisableAppLockConfirmationVisible = false;
        CancelCodeRefresh();
        ClearQrImage();
        ClearNotification();
        await RefreshUnlockMethodAvailabilityAsync();
        RefreshUnlockMethodState();
        NotifyAppLockChanged();
        NotifyUnlockedSectionChanged();
    }

    public Task ShowSettingsCategoriesAsync()
    {
        if (!IsSettingsVisible || IsBusy) return Task.CompletedTask;
        ClearMasterPasswordChangeInputs();
        _settingsCategory = MobileSettingsCategory.None;
        ClearNotification();
        NotifySettingsCategoryChanged();
        return Task.CompletedTask;
    }

    private Task ShowSettingsCategoryAsync(MobileSettingsCategory category)
    {
        if (!IsSettingsVisible || IsBusy || category == MobileSettingsCategory.None)
            return Task.CompletedTask;
        if (_settingsCategory == MobileSettingsCategory.Security
            && category != MobileSettingsCategory.Security)
        {
            ClearMasterPasswordChangeInputs();
        }
        _settingsCategory = category;
        ClearNotification();
        NotifySettingsCategoryChanged();
        return Task.CompletedTask;
    }

    public Task NavigateBackAsync() => IsSettingsCategoryDetailVisible
        ? ShowSettingsCategoriesAsync()
        : IsSettingsVisible
            ? ShowAccountsAsync()
            : Task.CompletedTask;

    public async Task<bool> TryHandleBackNavigationAsync()
    {
        if (IsAccountEditorExitConfirmationVisible)
        {
            await CancelAccountNavigationAsync();
            return true;
        }

        if (IsEditorVisible)
        {
            if (IsBusy) return true;
            if (HasUnsavedAccountChanges)
            {
                IsAccountEditorExitConfirmationVisible = true;
                return true;
            }

            await CancelEditAsync();
            return true;
        }

        if (!IsSettingsVisible) return false;
        if (!IsBusy) await NavigateBackAsync();
        return true;
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
                OnPropertyChanged(nameof(SelectedLanguage));
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
            OnPropertyChanged(nameof(SelectedLanguage));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectMinimumLogLevelAsync(AppLogLevel level)
    {
        if (!IsSettingsVisible || IsBusy || _settings.Current.MinimumLogLevel == level)
            return;

        var previousLevel = _settings.Current.MinimumLogLevel;
        IsBusy = true;
        try
        {
            _settings.Current.MinimumLogLevel = level;
            OnPropertyChanged(nameof(MinimumLogLevel));
            var saved = await _settings.SaveAsync();
            if (saved.IsSuccess)
            {
                ClearNotification();
                return;
            }

            _settings.Current.MinimumLogLevel = previousLevel;
            OnPropertyChanged(nameof(MinimumLogLevel));
            SetError(MobileStringKeys.SettingsSaveFailed);
        }
        catch (Exception)
        {
            _settings.Current.MinimumLogLevel = previousLevel;
            OnPropertyChanged(nameof(MinimumLogLevel));
            SetError(MobileStringKeys.SettingsSaveFailed);
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
                    ClearAccountFiltersForReveal();
                    await LoadAccountsAsync(outcome.AccountId);
                    RequestAccountReveal(outcome.AccountId);
                    SetSuccess(MobileStringKeys.QrAccountAdded);
                    break;
                case QrAccountImportStatus.Updated:
                    ClearAccountFiltersForReveal();
                    await LoadAccountsAsync(outcome.AccountId);
                    RequestAccountReveal(outcome.AccountId);
                    SetSuccess(MobileStringKeys.QrAccountUpdated);
                    break;
                case QrAccountImportStatus.KeptBoth:
                    ClearAccountFiltersForReveal();
                    await LoadAccountsAsync(outcome.AccountId);
                    RequestAccountReveal(outcome.AccountId);
                    SetSuccess(MobileStringKeys.QrAccountKeptBoth);
                    break;
                case QrAccountImportStatus.DuplicateUnchanged:
                    ClearAccountFiltersForReveal();
                    await LoadAccountsAsync(outcome.AccountId);
                    RequestAccountReveal(outcome.AccountId);
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

            var suggestedFileName = string.Format(
                Get(MobileStringKeys.BackupFileName),
                _timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            document = await _documents.CreateEncryptedBackupAsync(
                suggestedFileName,
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

            SetNotification(
                string.Format(
                    Get(MobileStringKeys.BackupExported),
                    string.IsNullOrWhiteSpace(document.Name) ? suggestedFileName : document.Name),
                NotificationSeverity.Success);
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
            BeginImportProgress(MobileStringKeys.ImportingAccounts);
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

            var imported = await _accountImport.ImportWithConflictResolutionAsync(
                decoded.Value,
                ResolveBackupImportAsync,
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

            if (outcome.Added == 0 && outcome.Replaced == 0)
                return;

            await LoadAccountsAsync();
            SetNotification(
                string.Format(
                    Get(MobileStringKeys.BackupImported),
                    outcome.Added,
                    outcome.Replaced,
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
            EndImportProgress();
            EndSensitiveOperation(operation);
            password = string.Empty;
            CompleteImportConfirmation(false);
            CompleteBackupConflictResolution(null);
            IsBusy = false;
            TryStartAutomaticBiometricUnlock();
        }
    }

    public async Task ImportAccountFileAsync()
    {
        if (!IsSettingsVisible || IsBusy) return;

        IsBusy = true;
        using var operation = BeginSensitiveOperation();
        try
        {
            using var document = await _documents.OpenAccountImportAsync(operation.Token);
            if (document is null) return;
            BeginImportProgress(MobileStringKeys.ImportingAccounts);
            if (!_authorization.State.IsUnlocked || !IsSettingsVisible)
            {
                SetError(MobileStringKeys.BackupRetryAfterUnlock);
                return;
            }

            if (string.IsNullOrWhiteSpace(document.Name))
            {
                SetError(MobileStringKeys.AccountFileImportRejected);
                return;
            }

            var decoded = await _exportService.ImportFromStreamAsync(
                document.Stream,
                document.Name,
                cancellationToken: operation.Token);
            if (decoded.IsFailed || decoded.Value.Count == 0)
            {
                SetError(MobileStringKeys.AccountFileImportRejected);
                return;
            }

            var imported = await _accountImport.ImportWithConflictResolutionAsync(
                decoded.Value,
                ResolveBackupImportAsync,
                operation.Token);
            if (imported.IsFailed)
            {
                SetError(MobileStringKeys.AccountFileImportFailed);
                return;
            }

            var outcome = imported.Value;
            if (outcome.Status == AccountImportStatus.Cancelled)
            {
                SetNotification(
                    Get(MobileStringKeys.AccountFileImportCancelled),
                    NotificationSeverity.Information);
                return;
            }

            if (outcome.Status != AccountImportStatus.Completed || outcome.Failed > 0)
            {
                SetError(MobileStringKeys.AccountFileImportFailed);
                return;
            }

            if (outcome.Added == 0 && outcome.Replaced == 0) return;

            await LoadAccountsAsync();
            SetNotification(
                string.Format(
                    Get(MobileStringKeys.AccountFileImported),
                    outcome.Added,
                    outcome.Replaced,
                    outcome.Skipped),
                NotificationSeverity.Success);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.AccountFileImportFailed);
        }
        finally
        {
            EndImportProgress();
            EndSensitiveOperation(operation);
            CompleteImportConfirmation(false);
            CompleteBackupConflictResolution(null);
            IsBusy = false;
            TryStartAutomaticBiometricUnlock();
        }
    }

    public async Task SelectThemeAsync(AppThemePreference preference)
    {
        if (!IsSettingsVisible
            || IsBusy
            || _appearanceSettingsService is null
            || _appearanceSettingsService.ThemePreference == preference)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var saved = await _appearanceSettingsService.SetThemePreferenceAsync(preference);
            if (saved.IsFailed)
            {
                SetError(MobileStringKeys.SettingsSaveFailed);
                return;
            }

            NotifyThemeSelectionChanged();
            ClearNotification();
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.SettingsSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private MobileAsyncCommand SettingsCategoryCommand(MobileSettingsCategory category) =>
        new(
            () => ShowSettingsCategoryAsync(category),
            () => IsSettingsCategoryListVisible && !IsBusy);

    private void NotifySettingsCategoryChanged()
    {
        OnPropertyChanged(nameof(IsSettingsCategoryListVisible));
        OnPropertyChanged(nameof(IsSettingsCategoryDetailVisible));
        OnPropertyChanged(nameof(IsAppearanceSettingsVisible));
        OnPropertyChanged(nameof(IsBrandIconSettingsVisible));
        OnPropertyChanged(nameof(IsSecuritySettingsVisible));
        OnPropertyChanged(nameof(IsBackupSettingsVisible));
        OnPropertyChanged(nameof(IsImportExportSettingsVisible));
        OnPropertyChanged(nameof(IsMiscSettingsVisible));
        OnPropertyChanged(nameof(IsFaqSettingsVisible));
        OnPropertyChanged(nameof(SettingsCategoryTitle));
        NotifyCommands();
    }

    public async Task ImportBrandIconsAsync()
    {
        if (!IsSettingsVisible || IsBusy || _brandIconPackService is null) return;
        IsBusy = true;
        try
        {
            using var document = await _documents.OpenBrandIconPackAsync();
            if (document is null)
            {
                SetNotification(
                    Get(MobileStringKeys.NoBrandIconPackSelected),
                    NotificationSeverity.Information);
                return;
            }

            BeginImportProgress(MobileStringKeys.ImportingBrandIcons);
            var imported = await _brandIconPackService.ImportAsync(document.Stream, document.Name);
            if (imported.IsFailed)
            {
                SetError(MobileStringKeys.BrandIconPackImportFailed);
                return;
            }

            SetNotification(
                string.Format(
                    Get(MobileStringKeys.BrandIconPackImported),
                    imported.Value.BrandCount,
                    imported.Value.ProviderDisplayName,
                    imported.Value.Version),
                NotificationSeverity.Success);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.BrandIconPackImportFailed);
        }
        finally
        {
            EndImportProgress();
            IsBusy = false;
        }
    }

    public async Task ResetBrandIconsAsync()
    {
        if (!IsSettingsVisible || IsBusy || _brandIconPackService is null) return;
        IsBusy = true;
        try
        {
            BeginImportProgress(MobileStringKeys.RemovingBrandIcons);
            var result = await _brandIconPackService.ResetAsync();
            SetNotification(
                Get(result.IsSuccess
                    ? MobileStringKeys.BrandIconPackReset
                    : MobileStringKeys.BrandIconPackResetFailed),
                result.IsSuccess ? NotificationSeverity.Success : NotificationSeverity.Error);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.BrandIconPackResetFailed);
        }
        finally
        {
            EndImportProgress();
            IsBusy = false;
        }
    }

    public Task ResolveImportConfirmationAsync(bool confirmed)
    {
        CompleteImportConfirmation(confirmed);
        return Task.CompletedTask;
    }

    public Task SelectAllBackupConflictsAsync(AccountImportConflictAction action)
    {
        if (!IsBackupConflictResolutionVisible) return Task.CompletedTask;
        foreach (var conflict in BackupImportConflicts)
            conflict.Select(action);
        OnPropertyChanged(nameof(IsAllBackupConflictsSkipped));
        OnPropertyChanged(nameof(IsAllBackupConflictsReplaced));
        _confirmBackupConflictResolutionCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    public Task ConfirmBackupConflictResolutionAsync()
    {
        if (!IsBackupConflictResolutionVisible
            || BackupImportConflicts.All(value => value.IsSkipSelected))
        {
            return Task.CompletedTask;
        }
        CompleteBackupConflictResolution(new AccountImportResolution(
            BackupImportConflicts.Select(value => value.ToResolution()).ToList()));
        return Task.CompletedTask;
    }

    public Task CancelBackupConflictResolutionAsync()
    {
        CompleteBackupConflictResolution(null);
        return Task.CompletedTask;
    }

    public Task ResolveQrConflictAsync(QrAccountConflictDecision decision)
    {
        CompleteQrConflict(decision);
        return Task.CompletedTask;
    }

    public Task BeginAddGroupAsync()
    {
        if (!IsAccountListVisible || IsBusy || _allAccounts.Count == 0)
            return Task.CompletedTask;

        ClearGroupEditor();
        RefreshGroupColorOptions(null);
        PopulateGroupEditorAccounts(null);
        CancelCodeRefresh();
        IsGroupEditorVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task BeginEditGroupAsync(Guid groupId)
    {
        if (!IsAccountListVisible || IsBusy) return Task.CompletedTask;

        var group = Groups.FirstOrDefault(item => item.Id == groupId)?.Group;
        if (group is null) return Task.CompletedTask;

        ClearGroupEditor();
        _editingGroupId = group.Id;
        GroupEditorName = group.Name;
        RefreshGroupColorOptions(group.Color);
        PopulateGroupEditorAccounts(group.Id);
        CancelCodeRefresh();
        IsGroupEditorVisible = true;
        ClearNotification();
        return Task.CompletedTask;
    }

    public async Task SaveGroupAsync()
    {
        if (!IsGroupEditorVisible || IsBusy) return;

        var name = GroupEditorName.Trim();
        if (name.Length == 0)
        {
            GroupEditorMessage = Get(MobileStringKeys.GroupNameRequired);
            return;
        }

        if (Groups.Any(group => group.Id != _editingGroupId
                && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            GroupEditorMessage = Get(MobileStringKeys.GroupNameDuplicate);
            return;
        }

        var selectedAccountIds = _allGroupEditorAccounts
            .Where(account => account.IsSelected)
            .Select(account => account.AccountId)
            .ToArray();
        if (selectedAccountIds.Length == 0)
        {
            GroupEditorMessage = Get(MobileStringKeys.GroupAccountRequired);
            return;
        }

        var color = SelectedGroupColor ?? GroupColorOptions.FirstOrDefault();
        if (color is null)
        {
            GroupEditorMessage = Get(MobileStringKeys.GroupSaveFailed);
            return;
        }

        var group = new AccountGroup(_editingGroupId ?? Guid.NewGuid(), name, color.Hex);
        IsBusy = true;
        try
        {
            var result = await _accountManager.SaveGroupAsync(group, selectedAccountIds);
            if (result.IsFailed)
            {
                GroupEditorMessage = Get(MobileStringKeys.GroupSaveFailed);
                return;
            }

            _selectedGroupId = group.Id;
            _showFavoritesOnly = false;
            var selectedIds = selectedAccountIds.ToHashSet();
            foreach (var account in _allAccounts)
            {
                if (selectedIds.Contains(account.Id))
                    account.UpdateGroup(group);
                else if (account.Group?.Id == group.Id)
                    account.UpdateGroup(null);
            }

            ClearGroupEditor();
            IsGroupEditorVisible = false;
            RefreshGroups();
            ApplyAccountFilter();
            RequestGroupReveal(group.Id);
            StartCodeRefresh();
        }
        catch (Exception)
        {
            GroupEditorMessage = Get(MobileStringKeys.GroupSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CancelGroupEditAsync()
    {
        if (!IsGroupEditorVisible || IsBusy) return Task.CompletedTask;
        ClearGroupEditor();
        IsGroupEditorVisible = false;
        ClearNotification();
        StartCodeRefresh();
        return Task.CompletedTask;
    }

    public Task BeginDeleteGroupAsync()
    {
        if (!IsGroupEditorVisible || !IsEditingGroup || IsBusy)
            return Task.CompletedTask;

        IsDeleteGroupConfirmationVisible = true;
        return Task.CompletedTask;
    }

    public async Task ConfirmDeleteGroupAsync()
    {
        if (!IsDeleteGroupConfirmationVisible || !_editingGroupId.HasValue || IsBusy)
            return;

        var groupId = _editingGroupId.Value;
        IsBusy = true;
        try
        {
            var result = await _accountManager.DeleteGroupAsync(groupId);
            if (result.IsFailed)
            {
                GroupEditorMessage = Get(MobileStringKeys.GroupDeleteFailed);
                IsDeleteGroupConfirmationVisible = false;
                return;
            }

            if (_selectedGroupId == groupId) _selectedGroupId = null;
            ClearGroupEditor();
            IsGroupEditorVisible = false;
            await LoadAccountsAsync();
        }
        catch (Exception)
        {
            GroupEditorMessage = Get(MobileStringKeys.GroupDeleteFailed);
            IsDeleteGroupConfirmationVisible = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CancelDeleteGroupAsync()
    {
        if (!IsBusy) IsDeleteGroupConfirmationVisible = false;
        return Task.CompletedTask;
    }

    public Task BeginAddAsync()
    {
        if (!CanEditAccounts()) return Task.CompletedTask;
        ClearEditor();
        RefreshBrandIconOptions(null);
        IsDeleteConfirmationVisible = false;
        IsEditorVisible = true;
        CaptureAccountEditorBaseline();
        ClearNotification();
        return Task.CompletedTask;
    }

    public Task BeginEditAsync()
    {
        if (!CanEditAccounts() || SelectedAccount is null) return Task.CompletedTask;
        ClearQrImage();
        _editingAccountId = SelectedAccount.Id;
        OnPropertyChanged(nameof(IsEditingExistingAccount));
        EditorIssuer = SelectedAccount.Issuer;
        EditorAccountName = SelectedAccount.AccountName;
        EditorSecret = string.Empty;
        EditorPeriodSeconds = SelectedAccount.ConfiguredPeriodSeconds;
        EditorIsFavorite = SelectedAccount.IsFavorite;
        RefreshBrandIconOptions(_brandIconPackService?.GetAccountBrandId(SelectedAccount.Id));
        IsDeleteConfirmationVisible = false;
        IsEditorVisible = true;
        CaptureAccountEditorBaseline();
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
            EditorIssuerMessage = Get(MobileStringKeys.IssuerRequired);
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
                EditorSecretMessage = Get(MobileStringKeys.SecretRequired);
                return;
            }

            if (!SecretValidation.IsValidBase32Secret(secret))
            {
                EditorSecretMessage = Get(MobileStringKeys.SecretInvalid);
                return;
            }
            if (EditorPeriodSeconds is not int periodSeconds
                || !TotpPeriodPolicy.IsSupported(periodSeconds))
            {
                IsAdvancedOptionsExpanded = true;
                EditorPeriodMessage = Get(MobileStringKeys.TotpPeriodInvalid);
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
                periodSeconds,
                group: existing?.Group,
                isFavorite: EditorIsFavorite);
            var saved = existing is null
                ? await _accountManager.AddNewAsync(updated)
                : await _accountManager.UpdateAsync(existing, updated);
            if (saved.IsFailed)
            {
                SetError(MobileStringKeys.AccountSaveFailed);
                return;
            }

            var iconPreferenceSaved = true;
            if (_brandIconPackService is not null)
            {
                try
                {
                    iconPreferenceSaved = (await _brandIconPackService.SetAccountBrandIdAsync(
                        updated.ID,
                        SelectedEditorBrandIconOption?.Id)).IsSuccess;
                }
                catch (Exception)
                {
                    iconPreferenceSaved = false;
                }
            }

            var savedId = updated.ID;
            var refreshCode = existing is null
                || !string.Equals(existing.Secret, updated.Secret, StringComparison.Ordinal)
                || existing.PeriodSeconds != updated.PeriodSeconds;
            ApplySavedAccount(updated);
            if (refreshCode)
                await RefreshSavedAccountCodeAsync(savedId);
#if DEBUG
            TraceCodeRefresh(refreshCode ? "save-targeted-refresh" : "save-metadata-only");
#endif
            ClearEditor();
            IsEditorVisible = false;
            RequestAccountReveal(savedId);
            SetSuccess(iconPreferenceSaved
                ? MobileStringKeys.AccountSaved
                : MobileStringKeys.AccountSavedIconPreferenceFailed);
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
        return Task.CompletedTask;
    }

    public async Task SaveAccountAndNavigateBackAsync()
    {
        if (!IsAccountEditorExitConfirmationVisible || IsBusy) return;
        IsAccountEditorExitConfirmationVisible = false;
        await SaveAccountAsync();
    }

    public Task DiscardAccountChangesAsync()
    {
        if (!IsAccountEditorExitConfirmationVisible || IsBusy) return Task.CompletedTask;
        IsAccountEditorExitConfirmationVisible = false;
        return CancelEditAsync();
    }

    public Task CancelAccountNavigationAsync()
    {
        if (!IsBusy) IsAccountEditorExitConfirmationVisible = false;
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

            if (_brandIconPackService is not null)
            {
                try
                {
                    await _brandIconPackService.SetAccountBrandIdAsync(accountId, null);
                }
                catch (Exception)
                {
                    // Account deletion is authoritative; a stale local display preference is harmless.
                }
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

            if (await CopyCodeCoreAsync(code))
                ShowCopyConfirmation(account);
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

    public async Task ToggleAccountFavoriteAsync(MobileAccountItem? account)
    {
        if (account is null
            || !CanEditAccounts()
            || _isFavoriteUpdateInProgress
            || !_allAccounts.Contains(account))
        {
            return;
        }

        _isFavoriteUpdateInProgress = true;
        ClearNotification();
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            var existing = loaded.IsSuccess
                ? loaded.Value.FirstOrDefault(value => value.ID == account.Id)
                : null;
            if (existing is null)
            {
                SetError(MobileStringKeys.FavoriteUpdateFailed);
                return;
            }

            var isFavorite = !account.IsFavorite;
            var saved = await _accountManager.UpdateAsync(
                existing,
                existing.WithFavorite(isFavorite));
            if (saved.IsFailed)
            {
                SetError(MobileStringKeys.FavoriteUpdateFailed);
                return;
            }

            var wasFilteringFavorites = _showFavoritesOnly;
            account.UpdateFavorite(isFavorite);
            RefreshFavoriteState();
            if (wasFilteringFavorites)
                ApplyAccountFilter(account.Id);
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.FavoriteUpdateFailed);
        }
        finally
        {
            _isFavoriteUpdateInProgress = false;
        }
    }

    public void SetAccountListScrolling(bool isScrolling)
    {
        if (_isAccountListScrolling == isScrolling) return;
        _isAccountListScrolling = isScrolling;
    }

    public void SetRealizedAccounts(IReadOnlyCollection<MobileAccountItem> realizedAccounts)
    {
        ArgumentNullException.ThrowIfNull(realizedAccounts);
        if (_isAccountListScrolling || Accounts.Count <= FullListCodeRefreshLimit) return;

        var realizedIds = realizedAccounts
            .Where(Accounts.Contains)
            .Select(account => account.Id)
            .ToHashSet();
        if (_realizedAccountIds.SetEquals(realizedIds)) return;

        _realizedAccountIds.Clear();
        _realizedAccountIds.UnionWith(realizedIds);
        foreach (var account in realizedAccounts)
            account.RefreshCodeBindings();
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
        _brandIconResolver.CatalogChanged -= BrandCatalogChanged;
        _sensitiveOperationLifetime?.Cancel();
        EndImportProgress();
        CancelNotificationLifetime();
        ClearCopyConfirmation();
        CancelBackgroundLockTimer();
        CancelCodeRefresh();
        ClearQrImage();
        ClearEditor();
        ClearGroupEditor();
        ClearPasswordInputs();
        Accounts.Clear();
        Groups.Clear();
        _authorization.Lock();
        CompleteQrConflict(QrAccountConflictDecision.Cancel);
        CompleteImportConfirmation(false);
        CompleteBackupConflictResolution(null);
    }

    private async Task LoadAccountsAsync(Guid? selectedId = null)
    {
        var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
        if (loaded.IsFailed)
        {
            SetError(MobileStringKeys.LoadingAccountsFailed);
            return;
        }

        _hasLoadedAccounts = true;
        var existingAccounts = _allAccounts.ToDictionary(account => account.Id);
        var refreshedAccounts = new List<MobileAccountItem>(loaded.Value.Count);
        foreach (var account in loaded.Value)
        {
            AccountGroupPolicy.TryNormalizeStored(account.Group, out var group);
            var accountName = account.AccountName ?? string.Empty;
            MobileAccountItem item;
            if (existingAccounts.TryGetValue(account.ID, out var existing)
                && string.Equals(existing.Issuer, account.Issuer, StringComparison.Ordinal)
                && string.Equals(existing.AccountName, accountName, StringComparison.Ordinal)
                && existing.ConfiguredPeriodSeconds == account.PeriodSeconds)
            {
                item = existing;
                item.UpdateCustomPeriodLabel(FormatCustomPeriod(account.PeriodSeconds));
                item.UpdateFavorite(account.IsFavorite);
                item.UpdateFavoriteLocalization(AddToFavoritesText, RemoveFromFavoritesText);
                item.UpdateGroup(group);
            }
            else
            {
                item = new MobileAccountItem(
                    account.ID,
                    account.Issuer,
                    accountName,
                    account.PeriodSeconds,
                    FormatCustomPeriod(account.PeriodSeconds),
                    _brandIconResolver.ResolveAccount(
                        account.ID,
                        account.Issuer,
                        account.AccountName),
                    account.IsFavorite,
                    AddToFavoritesText,
                    RemoveFromFavoritesText,
                    group);
            }

            item.UpdateLogoVisibility(ShowIssuerLogo);
            refreshedAccounts.Add(item);
        }
        _allAccounts.Clear();
        _allAccounts.AddRange(refreshedAccounts);

        if (_showFavoritesOnly
            && selectedId.HasValue
            && _allAccounts.FirstOrDefault(account => account.Id == selectedId.Value)?.IsFavorite != true)
        {
            _showFavoritesOnly = false;
        }
        RefreshFavoriteState();
        RefreshGroups();
        ApplyAccountFilter(selectedId);
        StartCodeRefresh();
    }

    private void ApplySavedAccount(Account account)
    {
        AccountGroupPolicy.TryNormalizeStored(account.Group, out var group);
        var accountName = account.AccountName ?? string.Empty;
        var item = _allAccounts.FirstOrDefault(value => value.Id == account.ID);
        var previousIssuer = item?.Issuer;
        if (item is null)
        {
            item = new MobileAccountItem(
                account.ID,
                account.Issuer,
                accountName,
                account.PeriodSeconds,
                FormatCustomPeriod(account.PeriodSeconds),
                _brandIconResolver.ResolveAccount(
                    account.ID,
                    account.Issuer,
                    account.AccountName),
                account.IsFavorite,
                AddToFavoritesText,
                RemoveFromFavoritesText,
                group);
            item.UpdateLogoVisibility(ShowIssuerLogo);
            _allAccounts.Add(item);
        }
        else
        {
            item.UpdateAccountDetails(
                account.Issuer,
                accountName,
                account.PeriodSeconds,
                FormatCustomPeriod(account.PeriodSeconds),
                _brandIconResolver.ResolveAccount(
                    account.ID,
                    account.Issuer,
                    account.AccountName));
            item.UpdateFavorite(account.IsFavorite);
            item.UpdateGroup(group);
        }

        if (previousIssuer is null
            || !string.Equals(previousIssuer, account.Issuer, StringComparison.OrdinalIgnoreCase))
        {
            RepositionAccountByIssuer(item);
        }
        if (_showFavoritesOnly && !account.IsFavorite)
            _showFavoritesOnly = false;
        RefreshFavoriteState();
        ApplyAccountFilter(account.ID);
    }

    private void RepositionAccountByIssuer(MobileAccountItem item)
    {
        _allAccounts.Remove(item);
        var insertionIndex = _allAccounts.FindLastIndex(candidate =>
            string.Compare(
                candidate.Issuer,
                item.Issuer,
                StringComparison.OrdinalIgnoreCase) <= 0) + 1;
        _allAccounts.Insert(insertionIndex, item);
    }

    private void ApplyAccountFilter(Guid? preferredSelection = null)
    {
        var selectedId = preferredSelection ?? SelectedAccount?.Id;
        var query = SearchText.Trim();
        var matches = _allAccounts.Where(account =>
            (!_showFavoritesOnly || account.IsFavorite)
            && (!_selectedGroupId.HasValue || account.Group?.Id == _selectedGroupId)
            && (query.Length == 0
                || account.Issuer.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || account.AccountName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (account.Group?.Name.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase) ?? false)))
            .ToArray();

        var visibleSequenceChanged = _accounts.Count != matches.Length;
        for (var index = 0; !visibleSequenceChanged && index < matches.Length; index++)
            visibleSequenceChanged = !ReferenceEquals(_accounts[index], matches[index]);
        if (visibleSequenceChanged)
        {
            _realizedAccountIds.Clear();
            _accounts.ReplaceAll(matches);
        }

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(IsNativeAccountListVisible));
        OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(SearchResultSummary));
        SelectedAccount = selectedId.HasValue
            ? Accounts.FirstOrDefault(account => account.Id == selectedId.Value)
                ?? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault();
        NotifyCommands();
    }

    private Task SelectGroupAsync(Guid groupId)
    {
        _selectedGroupId = _selectedGroupId == groupId ? null : groupId;
        _showFavoritesOnly = false;
        RefreshGroups();
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyAccountFilter();
        return Task.CompletedTask;
    }

    public async Task ImportCustomIconAsync()
    {
        if (!IsEditorVisible
            || IsBusy
            || !_editingAccountId.HasValue
            || _brandIconPackService is null)
        {
            return;
        }

        IsBusy = true;
        using var operation = BeginSensitiveOperation();
        try
        {
            using var document = await _documents.OpenCustomSvgIconAsync(operation.Token);
            if (document is null) return;
            var imported = await _brandIconPackService.ImportCustomIconAsync(
                _editingAccountId.Value,
                document.Stream,
                document.Name,
                operation.Token);
            if (imported.IsFailed)
            {
                SetError(CustomIconFailureKey(imported.Errors));
                return;
            }

            RefreshBrandIconOptions(imported.Value.Id, imported.Value);
            SetSuccess(MobileStringKeys.CustomIconImported);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.CustomIconImportFailed);
        }
        finally
        {
            EndSensitiveOperation(operation);
            IsBusy = false;
        }
    }

    private static string CustomIconFailureKey(IReadOnlyCollection<FluentResults.IError> errors)
    {
        var reason = errors.OfType<CustomIconImportError>().FirstOrDefault()?.Reason;
        return reason switch
        {
            CustomIconImportFailureReason.Empty => MobileStringKeys.CustomIconImportEmpty,
            CustomIconImportFailureReason.TooLarge => MobileStringKeys.CustomIconImportTooLarge,
            CustomIconImportFailureReason.MalformedXml => MobileStringKeys.CustomIconImportMalformed,
            CustomIconImportFailureReason.MissingVectorPath => MobileStringKeys.CustomIconImportMissingPath,
            CustomIconImportFailureReason.UnsafeContent => MobileStringKeys.CustomIconImportUnsafe,
            CustomIconImportFailureReason.Unreadable => MobileStringKeys.CustomIconImportUnreadable,
            _ => MobileStringKeys.CustomIconImportFailed
        };
    }

    private void RefreshGroups()
    {
        var groups = _allAccounts
            .Where(account => account.Group is not null)
            .GroupBy(account => account.Group!.Id)
            .Select(group =>
            {
                var stored = group.First().Group!;
                var normalized = new AccountGroup(
                    stored.Id,
                    stored.Name,
                    AccountGroupPolicy.NormalizeColor(stored.Color));
                return new MobileAccountGroupItem(
                    normalized,
                    group.Count(),
                    normalized.Id == _selectedGroupId,
                    new MobileAsyncCommand(
                        () => SelectGroupAsync(normalized.Id),
                        () => IsAccountListVisible && !IsBusy),
                    new MobileAsyncCommand(
                        () => BeginEditGroupAsync(normalized.Id),
                        () => IsAccountListVisible && !IsBusy));
            })
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (_selectedGroupId.HasValue && groups.All(group => group.Id != _selectedGroupId))
            _selectedGroupId = null;

        _groups.ReplaceAll(groups);
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasAccountNavigationCards));
        OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
    }

    private bool TrySelectAccount(MobileAccountItem account)
    {
        if (!Accounts.Any(value => value.Id == account.Id)) return false;
        SelectedAccount = account;
        return true;
    }

    private async Task<bool> CopyCodeCoreAsync(string code)
    {
        try
        {
            var seconds = TOTP.Core.Validation.ClipboardLifetimePolicy.NormalizeSeconds(
                _settings.Current.ClearClipboardSeconds);
            var result = await _clipboard.CopyAndScheduleClearAsync(
                code,
                TimeSpan.FromSeconds(seconds));
            if (result.IsFailed)
            {
                SetError(MobileStringKeys.CodeCopyFailed);
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            SetError(MobileStringKeys.CodeCopyFailed);
            return false;
        }
    }

    private void StartCodeRefresh(Guid? initialAccountId = null)
    {
        StopCodeRefresh();
        if (_allAccounts.Count == 0 || !IsAccountListVisible) return;

        var refreshAccounts = _allAccounts.ToArray();
        if (refreshAccounts.Length == 0) return;
        var initialAccounts = initialAccountId.HasValue
            ? refreshAccounts.Where(account => account.Id == initialAccountId.Value).ToArray()
            : refreshAccounts;
        foreach (var account in initialAccounts)
            account.BeginCodeRefresh(ShouldNotifyCodeBindings(account));

        var lifetime = new CancellationTokenSource();
        _codeLifetime = lifetime;
#if DEBUG
        TraceCodeRefresh($"loop-start total={refreshAccounts.Length} initial={initialAccounts.Length}");
#endif
        _ = RunCodeRefreshAsync(initialAccounts, lifetime);
    }

    private async Task RunCodeRefreshAsync(
        MobileAccountItem[] initialAccounts,
        CancellationTokenSource lifetime)
    {
        try
        {
            if (initialAccounts.Length > 0)
                await RefreshAccountCodesAsync(initialAccounts, lifetime.Token);
            var expiringAccounts = new List<MobileAccountItem>(_allAccounts.Count);

            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), lifetime.Token);
                var refreshAccounts = _allAccounts.ToArray();
                expiringAccounts.Clear();
                foreach (var account in refreshAccounts)
                {
                    if (account.RemainingSeconds <= 1)
                        expiringAccounts.Add(account);
                    else
                        account.Tick(notifyBindings: ShouldNotifyCodeBindings(account));
                }

                if (expiringAccounts.Count > 0)
                    await RefreshAccountCodesAsync(expiringAccounts, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            foreach (var account in Accounts)
                account.ClearCode(notifyBindings: ShouldNotifyCodeBindings(account));
            SetError(MobileStringKeys.CodeUnavailable);
        }
        finally
        {
            if (ReferenceEquals(_codeLifetime, lifetime)) _codeLifetime = null;
            lifetime.Dispose();
        }
    }

    private async Task RefreshSavedAccountCodeAsync(Guid accountId)
    {
        var account = _allAccounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null) return;

        account.BeginCodeRefresh(ShouldNotifyCodeBindings(account));
        var cancellationToken = _codeLifetime?.Token ?? CancellationToken.None;
        await RefreshAccountCodesAsync([account], cancellationToken);
    }

    private async Task<bool> RefreshAccountCodesAsync(
        IReadOnlyList<MobileAccountItem> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0) return false;
#if DEBUG
        TraceCodeRefresh($"generate count={accounts.Count} editor={IsEditorVisible}");
#endif

        FluentResults.Result<AccountTotpGenerationBatch> refreshed;
        try
        {
            refreshed = await _accountTotp.GenerateManyAsync(
                accounts.Select(account => account.Id).ToArray());
        }
        catch (Exception)
        {
            foreach (var account in accounts)
                account.ClearCode(notifyBindings: ShouldNotifyCodeBindings(account));
            SetError(MobileStringKeys.CodeUnavailable);
            return false;
        }

        if (cancellationToken.IsCancellationRequested) return false;
        if (refreshed.IsFailed)
        {
            foreach (var account in accounts)
                account.ClearCode(notifyBindings: ShouldNotifyCodeBindings(account));
            SetError(MobileStringKeys.CodeUnavailable);
            return false;
        }

        var refreshFailed = refreshed.Value.FailedAccountIds.Count > 0;
        foreach (var account in accounts)
        {
            if (!refreshed.Value.Codes.TryGetValue(account.Id, out var generated))
            {
                account.ClearCode(notifyBindings: !_isAccountListScrolling);
                refreshFailed = true;
                continue;
            }

            account.UpdateCode(
                generated.Code,
                Math.Max(1, generated.RemainingSeconds),
                generated.PeriodSeconds,
                notifyBindings: ShouldNotifyCodeBindings(account));
        }

        if (refreshFailed)
            SetError(MobileStringKeys.CodeUnavailable);
        return true;
    }

#if DEBUG
    private static void TraceCodeRefresh(string message) =>
        Console.WriteLine($"OTP-HARBOR-REFRESH {DateTimeOffset.UtcNow:O} {message}");
#endif

    private void LockCore()
    {
        if (_disposed || !IsAppLockEnabled) return;
        CapturePostUnlockNavigation();
        _backgroundedAtTimestamp = null;
        CancelBackgroundLockTimer();
        _sensitiveOperationLifetime?.Cancel();
        CancelNotificationLifetime();
        ClearCopyConfirmation();
        _authorization.Lock();
        CancelCodeRefresh();
        ClearEditor();
        IsEditorVisible = false;
        ClearGroupEditor();
        IsGroupEditorVisible = false;
        IsDeleteConfirmationVisible = false;
        ClearPasswordInputs();
        SelectedAccount = null;
        Accounts.Clear();
        _allAccounts.Clear();
        _hasLoadedAccounts = false;
        Groups.Clear();
        SearchText = string.Empty;
        _showFavoritesOnly = false;
        _selectedGroupId = null;
        RefreshFavoriteState();
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

    private void CapturePostUnlockNavigation()
    {
        _resumeSelectedGroupId = _selectedGroupId;
        _resumeFavoritesFilter = _showFavoritesOnly;
        _resumeSettingsCategory = _settingsCategory;
        _resumeEntityId = null;
        _resumeTarget = IsSettingsVisible
            ? MobileResumeTarget.Settings
            : IsEditorVisible && _editingAccountId.HasValue
                ? MobileResumeTarget.AccountEditor
                : IsGroupEditorVisible && _editingGroupId.HasValue
                    ? MobileResumeTarget.GroupEditor
                    : MobileResumeTarget.Accounts;

        if (_resumeTarget == MobileResumeTarget.AccountEditor)
            _resumeEntityId = _editingAccountId;
        else if (_resumeTarget == MobileResumeTarget.GroupEditor)
            _resumeEntityId = _editingGroupId;
    }

    private async Task RestorePostUnlockNavigationAsync()
    {
        var target = _resumeTarget;
        var entityId = _resumeEntityId;
        var selectedGroupId = _resumeSelectedGroupId;
        var favoritesFilter = _resumeFavoritesFilter;
        var settingsCategory = _resumeSettingsCategory;
        ClearPostUnlockNavigation();

        _selectedGroupId = selectedGroupId.HasValue
            && Groups.Any(group => group.Id == selectedGroupId.Value)
                ? selectedGroupId
                : null;
        _showFavoritesOnly = favoritesFilter && HasFavoriteAccounts;
        if (_showFavoritesOnly) _selectedGroupId = null;
        RefreshGroups();
        ApplyAccountFilter();
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();

        switch (target)
        {
            case MobileResumeTarget.Settings:
                await ShowSettingsAsync();
                if (settingsCategory != MobileSettingsCategory.None)
                    await ShowSettingsCategoryAsync(settingsCategory);
                break;
            case MobileResumeTarget.AccountEditor when entityId.HasValue:
                SelectedAccount = Accounts.FirstOrDefault(account => account.Id == entityId.Value);
                await BeginEditAsync();
                break;
            case MobileResumeTarget.GroupEditor when entityId.HasValue:
                await BeginEditGroupAsync(entityId.Value);
                break;
        }
    }

    private void ClearPostUnlockNavigation()
    {
        _resumeTarget = MobileResumeTarget.None;
        _resumeSettingsCategory = MobileSettingsCategory.None;
        _resumeEntityId = null;
        _resumeSelectedGroupId = null;
        _resumeFavoritesFilter = false;
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
        StopCodeRefresh();
        foreach (var account in _allAccounts)
        {
            account.ClearCode(notifyBindings:
                _realizedAccountIds.Contains(account.Id)
                || Accounts.Count <= FullListCodeRefreshLimit);
        }
        _realizedAccountIds.Clear();
    }

    private void StopCodeRefresh()
    {
        var lifetime = _codeLifetime;
        _codeLifetime = null;
        lifetime?.Cancel();
    }

    private bool ShouldNotifyCodeBindings(MobileAccountItem account) =>
        !_isAccountListScrolling
        && (Accounts.Count <= FullListCodeRefreshLimit && Accounts.Contains(account)
            || _realizedAccountIds.Contains(account.Id));

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
        _accountEditorBaseline = null;
        IsAccountEditorExitConfirmationVisible = false;
        _editingAccountId = null;
        OnPropertyChanged(nameof(IsEditingExistingAccount));
        EditorIssuer = string.Empty;
        EditorAccountName = string.Empty;
        EditorSecret = string.Empty;
        EditorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
        EditorIsFavorite = false;
        IsAdvancedOptionsExpanded = false;
        EditorBrandIconOptions = [];
        SelectedEditorBrandIconOption = null;
        EditorIssuerMessage = string.Empty;
        EditorSecretMessage = string.Empty;
        EditorPeriodMessage = string.Empty;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSecretPlaceholder));
    }

    private void CaptureAccountEditorBaseline() =>
        _accountEditorBaseline = CaptureAccountEditorSnapshot();

    private AccountEditorSnapshot CaptureAccountEditorSnapshot() => new(
        EditorIssuer,
        EditorAccountName,
        EditorSecret,
        EditorPeriodSeconds,
        EditorIsFavorite,
        SelectedEditorBrandIconOption?.Id);

    private void RefreshBrandIconOptions(
        string? selectedBrandId,
        BrandDefinition? selectedDefinition = null)
    {
        var automatic = new MobileBrandIconOption(
            null,
            Get(MobileStringKeys.AutomaticBrandIcon));
        var available = _brandIconPackService?.AvailableBrands ?? [];
        var selectedCustom = selectedDefinition ?? (!string.IsNullOrWhiteSpace(selectedBrandId)
            ? _brandIconPackService?.Resolve(null, selectedBrandId)
            : null);
        EditorBrandIconOptions =
        [
            automatic,
            .. available.Select(brand => new MobileBrandIconOption(
                brand.Id,
                brand.DisplayName,
                brand.SourceFileName)),
            .. (selectedCustom is not null
                && available.All(brand => !string.Equals(
                    brand.Id,
                    selectedCustom.Id,
                    StringComparison.OrdinalIgnoreCase))
                    ? [new MobileBrandIconOption(
                        selectedCustom.Id,
                        Get(MobileStringKeys.CustomAccountIcon),
                        selectedCustom.SourceFileName
                            ?? selectedCustom.DisplayName + ".svg")]
                    : Array.Empty<MobileBrandIconOption>())
        ];
        SelectedEditorBrandIconOption = EditorBrandIconOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedBrandId, StringComparison.OrdinalIgnoreCase))
            ?? EditorBrandIconOptions.FirstOrDefault();
    }

    private void ClearGroupEditor()
    {
        _editingGroupId = null;
        GroupEditorName = string.Empty;
        GroupEditorSearchText = string.Empty;
        GroupEditorMessage = string.Empty;
        GroupColorOptions = [];
        SelectedGroupColor = null;
        _allGroupEditorAccounts.Clear();
        if (_groupEditorAccounts.Count > 0) _groupEditorAccounts.Clear();
        IsDeleteGroupConfirmationVisible = false;
        OnPropertyChanged(nameof(GroupEditorTitle));
        OnPropertyChanged(nameof(IsCreatingGroup));
        OnPropertyChanged(nameof(IsEditingGroup));
    }

    private void PopulateGroupEditorAccounts(Guid? groupId)
    {
        _allGroupEditorAccounts.Clear();
        foreach (var account in _allAccounts)
        {
            _allGroupEditorAccounts.Add(new MobileGroupAccountSelection(
                account.Id,
                account.Issuer,
                account.AccountName,
                account.Brand,
                account.ShowIssuerLogo,
                groupId.HasValue && account.Group?.Id == groupId));
        }

        ApplyGroupEditorSearch();
    }

    private void ApplyGroupEditorSearch()
    {
        var query = GroupEditorSearchText.Trim();
        var matches = _allGroupEditorAccounts.Where(account =>
            query.Length == 0
            || account.Issuer.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || account.AccountName.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        _groupEditorAccounts.ReplaceAll(matches);
    }

    private void RefreshGroupColorOptions(string? selectedColor)
    {
        GroupColorOptions =
        [
            new("#4C956C", Get(MobileStringKeys.GroupColorGreen)),
            new("#18A999", Get(MobileStringKeys.GroupColorTurquoise)),
            new("#4F6BED", Get(MobileStringKeys.GroupColorBlue)),
            new("#F59E0B", Get(MobileStringKeys.GroupColorOrange)),
            new("#E45757", Get(MobileStringKeys.GroupColorRed)),
            new("#B455C7", Get(MobileStringKeys.GroupColorPurple))
        ];
        SelectedGroupColor = GroupColorOptions.FirstOrDefault(option =>
            string.Equals(option.Hex, selectedColor, StringComparison.OrdinalIgnoreCase))
            ?? GroupColorOptions[0];
    }

    private void RefreshFavoriteState()
    {
        _favoriteCount = _allAccounts.Count(account => account.IsFavorite);
        if (_showFavoritesOnly && !HasFavoriteAccounts)
            _showFavoritesOnly = false;
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(HasFavoriteAccounts));
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(HasAccountNavigationCards));
        OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _toggleFavoritesFilterCommand.NotifyCanExecuteChanged();
    }

    private void ClearPasswordInputs()
    {
        SetupPassword = string.Empty;
        SetupConfirmation = string.Empty;
        UnlockPassword = string.Empty;
        BiometricRecoveryPassword = string.Empty;
        AppLockRecoveryPassword = string.Empty;
        ClearMasterPasswordChangeInputs();
        BackupPassword = string.Empty;
        BackupPasswordConfirmation = string.Empty;
        ImportPassword = string.Empty;
    }

    private void ClearMasterPasswordChangeInputs()
    {
        CurrentMasterPassword = string.Empty;
        NewMasterPassword = string.Empty;
        NewMasterPasswordConfirmation = string.Empty;
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
        && !_isFavoriteUpdateInProgress
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

    private async Task<AccountImportResolution?> ResolveBackupImportAsync(
        AccountImportPreview preview,
        CancellationToken cancellationToken)
    {
        if (preview.ChangedConflicts.Count == 0)
        {
            if (preview.NewCount == 0 && preview.UnchangedCount == preview.TotalCount)
            {
                await ShowImportConfirmationAsync(
                    Get(MobileStringKeys.NoImportIdentical),
                    cancellationToken,
                    acknowledgementOnly: true);
                return new AccountImportResolution([]);
            }

            var confirmed = await ConfirmImportAsync(preview, cancellationToken);
            return confirmed ? new AccountImportResolution([]) : null;
        }

        var completion = new TaskCompletionSource<AccountImportResolution?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _backupConflictResolutionCompletion = completion;
        ClearBackupImportConflicts();
        foreach (var conflict in preview.ChangedConflicts)
        {
            var item = new BackupImportConflictItem(
                conflict,
                CurrentAccountFormat,
                BackupAccountFormat,
                ChangedFieldsFormat,
                IssuerFieldText,
                AccountNameFieldText,
                SecretFieldText,
                PeriodFieldText,
                FavoriteFieldText,
                KeepAccountAutomationFormat,
                RestoreAccountAutomationFormat);
            item.PropertyChanged += OnBackupConflictSelectionChanged;
            BackupImportConflicts.Add(item);
        }
        OnPropertyChanged(nameof(IsAllBackupConflictsSkipped));
        OnPropertyChanged(nameof(IsAllBackupConflictsReplaced));
        _confirmBackupConflictResolutionCommand.NotifyCanExecuteChanged();
        BackupConflictResolutionText = string.Format(
            Get(MobileStringKeys.BackupConflictResolution),
            preview.TotalCount,
            preview.NewCount,
            preview.UnchangedCount,
            preview.ChangedConflictCount);
        IsBackupConflictResolutionVisible = true;

        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            return await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(_backupConflictResolutionCompletion, completion))
            {
                _backupConflictResolutionCompletion = null;
                ClearBackupImportConflicts();
                BackupConflictResolutionText = string.Empty;
                IsBackupConflictResolutionVisible = false;
            }
        }
    }

    private void BrandCatalogChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(HasImportedBrandIcons));
        OnPropertyChanged(nameof(BrandIconPackStatusText));
        if (_brandIconPackService is not null
            && _showIssuerLogo != _brandIconPackService.ShowIssuerLogo)
        {
            _showIssuerLogo = _brandIconPackService.ShowIssuerLogo;
            OnPropertyChanged(nameof(ShowIssuerLogo));
        }
        _resetBrandIconsCommand.NotifyCanExecuteChanged();
        if (IsEditorVisible)
        {
            RefreshBrandIconOptions(_editingAccountId.HasValue
                ? _brandIconPackService?.GetAccountBrandId(_editingAccountId.Value)
                : SelectedEditorBrandIconOption?.Id);
        }
        foreach (var account in _allAccounts)
            account.UpdateBrand(_brandIconResolver.ResolveAccount(
                account.Id,
                account.Issuer,
                account.AccountName));
    }

    private void UpdateLogoVisibility()
    {
        foreach (var account in _allAccounts)
            account.UpdateLogoVisibility(ShowIssuerLogo);
        OnPropertyChanged(nameof(ShowIssuerLogo));
    }

    private async Task SaveShowIssuerLogoAsync(bool requested, bool previous, long revision)
    {
        try
        {
            if (_brandIconPackService is null
                || (await _brandIconPackService.SetShowIssuerLogoAsync(requested)).IsFailed)
            {
                RestoreShowIssuerLogoAfterSaveFailure(previous, revision);
                SetError(MobileStringKeys.SettingsSaveFailed);
            }
        }
        catch (Exception)
        {
            RestoreShowIssuerLogoAfterSaveFailure(previous, revision);
            SetError(MobileStringKeys.SettingsSaveFailed);
        }
    }

    private void RestoreShowIssuerLogoAfterSaveFailure(bool previous, long revision)
    {
        if (_showIssuerLogoRevision != revision) return;
        _showIssuerLogo = previous;
        UpdateLogoVisibility();
    }

    private void CompleteBackupConflictResolution(AccountImportResolution? resolution) =>
        _backupConflictResolutionCompletion?.TrySetResult(resolution);

    private void OnBackupConflictSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BackupImportConflictItem.IsSkipSelected)
            or nameof(BackupImportConflictItem.IsReplaceSelected)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsAllBackupConflictsSkipped));
        OnPropertyChanged(nameof(IsAllBackupConflictsReplaced));
        _confirmBackupConflictResolutionCommand.NotifyCanExecuteChanged();
    }

    private void ClearBackupImportConflicts()
    {
        foreach (var conflict in BackupImportConflicts)
            conflict.PropertyChanged -= OnBackupConflictSelectionChanged;
        BackupImportConflicts.Clear();
        OnPropertyChanged(nameof(IsAllBackupConflictsSkipped));
        OnPropertyChanged(nameof(IsAllBackupConflictsReplaced));
        _confirmBackupConflictResolutionCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> ShowImportConfirmationAsync(
        string message,
        CancellationToken cancellationToken,
        bool acknowledgementOnly = false)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _importConfirmationCompletion = completion;
        _isImportConfirmationAcknowledgementOnly = acknowledgementOnly;
        OnPropertyChanged(nameof(IsImportConfirmationAcknowledgementOnly));
        OnPropertyChanged(nameof(IsImportConfirmationCancelVisible));
        OnPropertyChanged(nameof(ImportConfirmationTitle));
        OnPropertyChanged(nameof(ConfirmImportText));
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
                _isImportConfirmationAcknowledgementOnly = false;
                OnPropertyChanged(nameof(IsImportConfirmationAcknowledgementOnly));
                OnPropertyChanged(nameof(IsImportConfirmationCancelVisible));
                OnPropertyChanged(nameof(ImportConfirmationTitle));
                OnPropertyChanged(nameof(ConfirmImportText));
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
        OnPropertyChanged(nameof(IsNativeAccountListVisible));
        OnPropertyChanged(nameof(IsNativeAccountGroupsVisible));
        OnPropertyChanged(nameof(IsScreenCaptureProtectionRequired));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(CanHandleSystemBack));
        OnPropertyChanged(nameof(IsSettingsCategoryListVisible));
        OnPropertyChanged(nameof(IsSettingsCategoryDetailVisible));
        OnPropertyChanged(nameof(IsAppearanceSettingsVisible));
        OnPropertyChanged(nameof(IsBrandIconSettingsVisible));
        OnPropertyChanged(nameof(IsSecuritySettingsVisible));
        OnPropertyChanged(nameof(IsBackupSettingsVisible));
        OnPropertyChanged(nameof(IsImportExportSettingsVisible));
        OnPropertyChanged(nameof(IsMiscSettingsVisible));
        OnPropertyChanged(nameof(IsFaqSettingsVisible));
        OnPropertyChanged(nameof(SettingsCategoryTitle));
        OnPropertyChanged(nameof(IsBiometricSetupAvailable));
        OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
        OnPropertyChanged(nameof(IsBiometricUnavailable));
        OnPropertyChanged(nameof(IsDeviceCredentialUnavailable));
        OnPropertyChanged(nameof(IsUnlockMethodSelectionEnabled));
        NotifyCommands();
    }

    private void RequestAccountReveal(
        Guid accountId,
        bool highlight = true,
        bool alignToTop = false)
    {
        if (accountId == Guid.Empty || Accounts.All(account => account.Id != accountId)) return;
        AccountRevealRequest = new MobileAccountRevealRequest(
            accountId,
            ++_accountRevealRevision,
            highlight,
            alignToTop);
    }

    private void RequestGroupReveal(Guid groupId)
    {
        if (groupId == Guid.Empty || Groups.All(group => group.Id != groupId)) return;
        GroupRevealRequest = new MobileAccountGroupRevealRequest(
            groupId,
            ++_groupRevealRevision);
    }

    private void ClearAccountFiltersForReveal()
    {
        _showFavoritesOnly = false;
        _selectedGroupId = null;
        if (HasSearchText) SearchText = string.Empty;
        RefreshGroups();
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
    }

    private void NotifyAppLockChanged()
    {
        OnPropertyChanged(nameof(IsAppLockEnabled));
        OnPropertyChanged(nameof(IsAppLockDisabled));
        OnPropertyChanged(nameof(AppLockActionText));
        OnPropertyChanged(nameof(IsManualLockVisible));
        OnPropertyChanged(nameof(IsBiometricSetupAvailable));
        OnPropertyChanged(nameof(IsBiometricEnrollmentStartVisible));
        OnPropertyChanged(nameof(IsUnlockMethodSelectionEnabled));
        NotifyUnlockMethodChanged();
        NotifyCommands();
    }

    private bool CanSelectUnlockMethod(PreferredUnlockMethod method)
    {
        if (!IsSettingsVisible || !IsAppLockEnabled || IsBusy) return false;
        if (DisplayedUnlockMethod == method) return true;
        return method switch
        {
            PreferredUnlockMethod.Password => true,
            PreferredUnlockMethod.PlatformQuickUnlock => IsBiometricAvailable,
            PreferredUnlockMethod.PlatformDeviceCredential => IsDeviceCredentialAvailable,
            _ => false
        };
    }

    private void RefreshUnlockMethodState()
    {
        IsBiometricEnabled = IsAppLockEnabled
            && IsBiometricUnlockSelected
            && _authorization.State.ConfiguredGate == AuthorizationGateKind.Hello;
        NotifyUnlockMethodChanged();
    }

    private void NotifyUnlockMethodChanged()
    {
        OnPropertyChanged(nameof(IsPasswordUnlockSelected));
        OnPropertyChanged(nameof(IsBiometricUnlockSelected));
        OnPropertyChanged(nameof(IsDeviceCredentialUnlockSelected));
        OnPropertyChanged(nameof(IsDeviceCredentialEnabled));
        OnPropertyChanged(nameof(IsBiometricUnlockOptionEnabled));
        OnPropertyChanged(nameof(IsDeviceCredentialUnlockOptionEnabled));
        OnPropertyChanged(nameof(IsSelectedPlatformUnlockAvailable));
        NotifyPlatformUnlockVisibilityChanged();
        OnPropertyChanged(nameof(BiometricUnlockText));
        NotifyCommands();
    }

    private void NotifyPlatformUnlockVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsBiometricUnlockVisible));
        OnPropertyChanged(nameof(IsFingerprintUnlockVisible));
        OnPropertyChanged(nameof(IsDeviceCredentialUnlockVisible));
    }

    private void ResetPendingUnlockMethodChange()
    {
        _pendingUnlockMethod = DisplayedUnlockMethod;
        _isReenablingAppLock = false;
        BiometricRecoveryPassword = string.Empty;
        IsBiometricEnrollmentVisible = false;
        NotifyUnlockMethodChanged();
    }

    private PreferredUnlockMethod DisplayedUnlockMethod =>
        IsAppLockEnabled && _authorization.State.IsConfigured
            ? _authorization.State.PreferredUnlockMethod
            : _settings.Current.PreferredUnlockMethod;

    private void NotifyLocalizedTextChanged()
    {
        foreach (var propertyName in LocalizedTextProperties)
            OnPropertyChanged(propertyName);

        foreach (var account in _allAccounts)
        {
            account.UpdateCustomPeriodLabel(FormatCustomPeriod(account.ConfiguredPeriodSeconds));
            account.UpdateFavoriteLocalization(AddToFavoritesText, RemoveFromFavoritesText);
        }

        if (IsGroupEditorVisible)
            RefreshGroupColorOptions(SelectedGroupColor?.Hex);
        if (IsEditorVisible)
            RefreshBrandIconOptions(SelectedEditorBrandIconOption?.Id);

        // Language buttons bind to these computed selection properties. They are
        // state, not localized text, but must refresh together with the catalog.
        OnPropertyChanged(nameof(IsEnglishLanguageSelected));
        OnPropertyChanged(nameof(IsGermanLanguageSelected));
        OnPropertyChanged(nameof(IsFrenchLanguageSelected));
        OnPropertyChanged(nameof(IsSpanishLanguageSelected));
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(SelectedLanguage));

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
        NotifyPlatformUnlockVisibilityChanged();
        OnPropertyChanged(nameof(IsSelectedPlatformUnlockAvailable));
    }

    private void SetError(string key) =>
        SetNotification(Get(key), NotificationSeverity.Error);

    private void BeginImportProgress(string textKey)
    {
        EndImportProgress();
        ImportProgressText = Get(textKey);
        var lifetime = new CancellationTokenSource();
        _importProgressLifetime = lifetime;
        _ = ShowImportProgressAfterDelayAsync(lifetime);
    }

    private async Task ShowImportProgressAfterDelayAsync(CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(ImportProgressDelay, lifetime.Token);
            if (ReferenceEquals(_importProgressLifetime, lifetime) && !_disposed)
                IsImportProgressVisible = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_importProgressLifetime, lifetime))
                _importProgressLifetime = null;
            lifetime.Dispose();
        }
    }

    private void EndImportProgress()
    {
        var lifetime = _importProgressLifetime;
        _importProgressLifetime = null;
        lifetime?.Cancel();
        IsImportProgressVisible = false;
        ImportProgressText = string.Empty;
    }

    private void SetSuccess(string key) =>
        SetNotification(Get(key), NotificationSeverity.Success);

    private void SetNotification(string text, NotificationSeverity severity) =>
        SetTransientNotification(text, severity, NotificationDuration);

    private void SetTransientNotification(
        string text,
        NotificationSeverity severity,
        TimeSpan duration)
    {
        CancelNotificationLifetime();
        NotificationSeverity = severity;
        NotificationText = text;
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

    private void ShowCopyConfirmation(MobileAccountItem account)
    {
        ClearCopyConfirmation();
        account.ShowCopyConfirmation(Get(MobileStringKeys.CodeCopied));
        _copyConfirmationAccount = account;
        var lifetime = new CancellationTokenSource();
        _copyConfirmationLifetime = lifetime;
        _ = ClearCopyConfirmationAfterDelayAsync(account, lifetime);
    }

    private async Task ClearCopyConfirmationAfterDelayAsync(
        MobileAccountItem account,
        CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(CopyConfirmationDuration, lifetime.Token);
            if (ReferenceEquals(_copyConfirmationLifetime, lifetime))
            {
                account.ClearCopyConfirmation();
                _copyConfirmationAccount = null;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_copyConfirmationLifetime, lifetime))
                _copyConfirmationLifetime = null;
            lifetime.Dispose();
        }
    }

    private void ClearCopyConfirmation()
    {
        var lifetime = _copyConfirmationLifetime;
        _copyConfirmationLifetime = null;
        lifetime?.Cancel();
        _copyConfirmationAccount?.ClearCopyConfirmation();
        _copyConfirmationAccount = null;
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
        _beginDeviceCredentialEnrollmentCommand.NotifyCanExecuteChanged();
        _selectPasswordUnlockCommand.NotifyCanExecuteChanged();
        _enableBiometricCommand.NotifyCanExecuteChanged();
        _cancelBiometricEnrollmentCommand.NotifyCanExecuteChanged();
        _toggleAppLockCommand.NotifyCanExecuteChanged();
        _beginDisableAppLockCommand.NotifyCanExecuteChanged();
        _confirmDisableAppLockCommand.NotifyCanExecuteChanged();
        _cancelDisableAppLockCommand.NotifyCanExecuteChanged();
        _enableAppLockCommand.NotifyCanExecuteChanged();
        _changeMasterPasswordCommand.NotifyCanExecuteChanged();
        _lockCommand.NotifyCanExecuteChanged();
        _showAccountsCommand.NotifyCanExecuteChanged();
        _showSettingsCommand.NotifyCanExecuteChanged();
        _showSettingsCategoriesCommand.NotifyCanExecuteChanged();
        _showAppearanceSettingsCommand.NotifyCanExecuteChanged();
        _showBrandIconSettingsCommand.NotifyCanExecuteChanged();
        _showSecuritySettingsCommand.NotifyCanExecuteChanged();
        _showBackupSettingsCommand.NotifyCanExecuteChanged();
        _showImportExportSettingsCommand.NotifyCanExecuteChanged();
        _showMiscSettingsCommand.NotifyCanExecuteChanged();
        _showFaqSettingsCommand.NotifyCanExecuteChanged();
        _showImportFormatsFaqCommand.NotifyCanExecuteChanged();
        _clearSearchCommand.NotifyCanExecuteChanged();
        _clearGroupEditorSearchCommand.NotifyCanExecuteChanged();
        _toggleFavoritesFilterCommand.NotifyCanExecuteChanged();
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        _beginAddGroupCommand.NotifyCanExecuteChanged();
        _saveGroupCommand.NotifyCanExecuteChanged();
        _cancelGroupEditCommand.NotifyCanExecuteChanged();
        _beginDeleteGroupCommand.NotifyCanExecuteChanged();
        _confirmDeleteGroupCommand.NotifyCanExecuteChanged();
        _cancelDeleteGroupCommand.NotifyCanExecuteChanged();
        _beginAddCommand.NotifyCanExecuteChanged();
        _saveAccountCommand.NotifyCanExecuteChanged();
        _cancelEditCommand.NotifyCanExecuteChanged();
        _saveAccountAndNavigateBackCommand.NotifyCanExecuteChanged();
        _discardAccountChangesCommand.NotifyCanExecuteChanged();
        _cancelAccountNavigationCommand.NotifyCanExecuteChanged();
        _clearEditorPeriodCommand.NotifyCanExecuteChanged();
        _importCustomIconCommand.NotifyCanExecuteChanged();
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
        _importAccountFileCommand.NotifyCanExecuteChanged();
        _importBrandIconsCommand.NotifyCanExecuteChanged();
        _resetBrandIconsCommand.NotifyCanExecuteChanged();
        _confirmImportCommand.NotifyCanExecuteChanged();
        _cancelImportCommand.NotifyCanExecuteChanged();
        _skipAllBackupConflictsCommand.NotifyCanExecuteChanged();
        _replaceAllBackupConflictsCommand.NotifyCanExecuteChanged();
        _confirmBackupConflictResolutionCommand.NotifyCanExecuteChanged();
        _cancelBackupConflictResolutionCommand.NotifyCanExecuteChanged();
        _selectEnglishLanguageCommand.NotifyCanExecuteChanged();
        _selectGermanLanguageCommand.NotifyCanExecuteChanged();
        _selectFrenchLanguageCommand.NotifyCanExecuteChanged();
        _selectSpanishLanguageCommand.NotifyCanExecuteChanged();
        _selectSystemThemeCommand.NotifyCanExecuteChanged();
        _selectDarkThemeCommand.NotifyCanExecuteChanged();
        _selectLightThemeCommand.NotifyCanExecuteChanged();
    }

    private void NotifyThemeSelectionChanged()
    {
        OnPropertyChanged(nameof(IsSystemThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
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
        nameof(ChangeMasterPasswordText),
        nameof(ChangeMasterPasswordDescriptionText),
        nameof(CurrentPasswordText),
        nameof(NewPasswordText),
        nameof(RevealPasswordText),
        nameof(RevealPasswordHelpText),
        nameof(CreateVaultText),
        nameof(UnlockTitle),
        nameof(UnlockDescription),
        nameof(UnlockText),
        nameof(AccountsTitle),
        nameof(NoAccountsText),
        nameof(SetUpFirstAccountText),
        nameof(AddAccountText),
        nameof(EditAccountText),
        nameof(DeleteAccountText),
        nameof(LockText),
        nameof(IssuerText),
        nameof(AccountNameText),
        nameof(AdvancedOptionsText),
        nameof(TotpPeriodText),
        nameof(ClearPeriodText),
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
        nameof(UnlockMethodTitleText),
        nameof(UnlockMethodDescriptionText),
        nameof(UnlockWithPasswordText),
        nameof(UnlockWithBiometricsText),
        nameof(UnlockWithDevicePinText),
        nameof(DeviceCredentialUnlockButtonText),
        nameof(DevicePinUnavailableText),
        nameof(UnlockMethodPasswordPromptText),
        nameof(ApplyUnlockMethodText),
        nameof(CodesText),
        nameof(SettingsText),
        nameof(CloseSettingsText),
        nameof(BackToSettingsText),
        nameof(BackToTopText),
        nameof(AppearanceSettingsDescriptionText),
        nameof(BrandIconSettingsDescriptionText),
        nameof(SecuritySettingsDescriptionText),
        nameof(BackupSettingsDescriptionText),
        nameof(ImportExportSettingsDescriptionText),
        nameof(MiscellaneousText),
        nameof(MiscSettingsDescriptionText),
        nameof(LoggingLevelText),
        nameof(FaqText),
        nameof(FaqSettingsDescriptionText),
        nameof(FaqImportIconPacksQuestionText),
        nameof(FaqImportIconPacksAnswerText),
        nameof(FaqImportIconPacksSourcesText),
        nameof(FaqSimpleIconsOfficialLinkText),
        nameof(FaqAegisIconPackDocsLinkText),
        nameof(FaqImportIconPacksDisclaimerText),
        nameof(FaqImportFormatsQuestionText),
        nameof(FaqImportFormatsIntroText),
        nameof(FaqImportFormatsAegisTitleText),
        nameof(FaqImportFormatsAegisDescriptionText),
        nameof(FaqImportFormatsAegisExampleText),
        nameof(FaqImportFormatsTwoFasTitleText),
        nameof(FaqImportFormatsTwoFasDescriptionText),
        nameof(FaqImportFormatsTwoFasExampleText),
        nameof(FaqImportFormatsOtpAuthTitleText),
        nameof(FaqImportFormatsOtpAuthDescriptionText),
        nameof(FaqImportFormatsOtpAuthExampleText),
        nameof(SettingsCategoryTitle),
        nameof(LanguageText),
        nameof(EnglishLanguageText),
        nameof(GermanLanguageText),
        nameof(FrenchLanguageText),
        nameof(SpanishLanguageText),
        nameof(AppearanceText),
        nameof(ThemeFollowSystemText),
        nameof(ThemeDarkText),
        nameof(ThemeLightText),
        nameof(SecurityText),
        nameof(AppLockTitleText),
        nameof(AppLockEnabledDescriptionText),
        nameof(AppLockDisabledDescriptionText),
        nameof(DisableAppLockText),
        nameof(EnableAppLockText),
        nameof(AppLockActionText),
        nameof(DisableAppLockWarningText),
        nameof(SearchAccountsText),
        nameof(SearchBrandIconsText),
        nameof(ClearSearchText),
        nameof(FavoritesText),
        nameof(FavoriteAccountText),
        nameof(AddToFavoritesText),
        nameof(RemoveFromFavoritesText),
        nameof(CreateGroupText),
        nameof(EditGroupText),
        nameof(DeleteGroupText),
        nameof(GroupNameText),
        nameof(GroupColorText),
        nameof(GroupAccountsText),
        nameof(GroupEditorTitle),
        nameof(GroupDeletePrompt),
        nameof(SearchResultSummary),
        nameof(NoSearchResultsText),
        nameof(AccountSwipeHintText),
        nameof(ScanQrText),
        nameof(ImportExportText),
        nameof(ImportSectionText),
        nameof(ExportSectionText),
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
        nameof(BackupSectionText),
        nameof(RestoreSectionText),
        nameof(BackupDescription),
        nameof(ImportBackupDescriptionText),
        nameof(ImportAccountFileText),
        nameof(ImportAccountFileDescriptionText),
        nameof(ImportFormatAegisText),
        nameof(ImportFormatTwoFasText),
        nameof(ImportFormatOtpAuthText),
        nameof(ViewImportFormatsFaqText),
        nameof(ExportBackupDescriptionText),
        nameof(BackupPasswordText),
        nameof(ConfirmBackupPasswordText),
        nameof(ExportBackupText),
        nameof(ImportBackupText),
        nameof(BrandIconsText),
        nameof(BrandIconsDescriptionText),
        nameof(ChooseCustomSvgIconText),
        nameof(OrText),
        nameof(SelectedEditorCustomIconFileName),
        nameof(UnsavedAccountChangesTitleText),
        nameof(UnsavedAccountChangesPromptText),
        nameof(DiscardChangesText),
        nameof(BrandIconText),
        nameof(BrandIconHelpText),
        nameof(ImportSimpleIconsPackText),
        nameof(ResetBrandIconsText),
        nameof(ShowIssuerLogoText),
        nameof(ShowIssuerLogoDescriptionText),
        nameof(ImportConfirmationTitle),
        nameof(ConfirmImportText),
        nameof(BackupConflictResolutionTitle),
        nameof(ConflictAccountText),
        nameof(SkipText),
        nameof(OverrideText),
        nameof(SkipAllText),
        nameof(OverrideAllText),
        nameof(ApplyToAllText),
        nameof(CurrentAccountFormat),
        nameof(BackupAccountFormat),
        nameof(ChangedFieldsFormat),
        nameof(IssuerFieldText),
        nameof(AccountNameFieldText),
        nameof(SecretFieldText),
        nameof(PeriodFieldText),
        nameof(FavoriteFieldText),
        nameof(KeepAccountAutomationFormat),
        nameof(RestoreAccountAutomationFormat),
        nameof(EditorTitle),
        nameof(EditorSecretPlaceholder)
    ];

    private sealed record AccountEditorSnapshot(
        string Issuer,
        string AccountName,
        string Secret,
        int? PeriodSeconds,
        bool IsFavorite,
        string? BrandIconId);

    private enum MobileScreen
    {
        Starting,
        Setup,
        Unlock,
        Accounts
    }

    private enum MobileSettingsCategory
    {
        None,
        Appearance,
        BrandIcons,
        Security,
        Backups,
        ImportExport,
        Miscellaneous,
        Faq
    }

    private enum MobileResumeTarget
    {
        None,
        Accounts,
        Settings,
        AccountEditor,
        GroupEditor
    }
}
