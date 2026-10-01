using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;
using TOTP.Avalonia.Shared.Branding;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class AccountListViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAccountManager _accountManager;
    private readonly IAccountTotpService _accountTotpService;
    private readonly IAsyncClipboardService _clipboardService;
    private readonly IAccountQrCodeService _accountQrCodeService;
    private readonly IAvaloniaQrImageFactory _qrImageFactory;
    private readonly IAvaloniaDialogService _dialogs;
    private readonly IAvaloniaLocalizationService _localization;
    private readonly IBrandIconResolver _brandIconResolver;
    private readonly IBrandIconPackService? _brandIconPackService;
    private readonly TimeSpan _countdownTickInterval;
    private readonly TimeSpan _copyConfirmationDuration;
    private readonly ISettingsService? _settingsService;
    private readonly IAvaloniaQrPreviewDialogService? _qrPreviewDialogs;
    private readonly AsyncCommand _loadCommand;
    private readonly AsyncCommand _generateCommand;
    private readonly AsyncCommand _copyCommand;
    private readonly AsyncCommand<AccountListItemViewModel> _copyAccountCodeCommand;
    private readonly AsyncCommand<AccountListItemViewModel> _toggleAccountFavoriteCommand;
    private readonly AsyncCommand _generateQrCommand;
    private readonly AsyncCommand _beginAddCommand;
    private readonly AsyncCommand _beginEditCommand;
    private readonly AsyncCommand _saveAccountCommand;
    private readonly AsyncCommand _cancelEditCommand;
    private readonly AsyncCommand _clearEditorPeriodCommand;
    private readonly AsyncCommand _deleteAccountCommand;
    private readonly AsyncCommand _beginContextEditCommand;
    private readonly AsyncCommand _generateContextQrCommand;
    private readonly AsyncCommand _deleteContextAccountCommand;
    private readonly AsyncCommand _beginAddGroupCommand;
    private readonly AsyncCommand _beginEditFavoritesCommand;
    private readonly AsyncCommand _saveGroupCommand;
    private readonly AsyncCommand _cancelGroupEditCommand;
    private readonly AsyncCommand _clearGroupFilterCommand;
    private readonly AsyncCommand _toggleFavoritesFilterCommand;
    private readonly AsyncCommand _selectUngroupedCommand;
    private readonly AsyncCommand _toggleAllAccountsFilterCommand;
    private CancellationTokenSource? _rowCodeLifetime;
    private CancellationTokenSource? _recentHighlightLifetime;
    private CancellationTokenSource? _copyConfirmationLifetime;
    private AccountListItemViewModel? _copyConfirmationAccount;
    private IReadOnlyList<AccountListItemViewModel> _allAccounts = [];
    private IReadOnlyList<AccountListItemViewModel> _accounts = [];
    private IReadOnlyList<AccountSortOption> _sortOptions = [];
    private AccountSortOption? _selectedSortOption;
    private IReadOnlyList<AccountGroupListItemViewModel> _allGroups = [];
    private IReadOnlyList<AccountGroupListItemViewModel> _groups = [];
    private Guid? _selectedGroupId;
    private bool _showFavoritesOnly;
    private bool _showAllAccounts;
    private string _searchText = string.Empty;
    private AccountListItemViewModel? _selectedAccount;
    private AccountListItemViewModel? _contextAccount;
    private string _generatedCode = string.Empty;
    private string _codeMessage = string.Empty;
    private string? _codeMessageLocalizationKey;
    private object[] _codeMessageLocalizationArguments = [];
    private string? _notificationLocalizationKey;
    private object[] _notificationLocalizationArguments = [];
    private bool _isBusy;
    private bool _isFavoriteUpdateInProgress;
    private bool _isGenerating;
    private int _remainingSeconds;
    private int _periodSeconds;
    private AvaloniaQrImageHandle? _qrImage;
    private bool _isEditorVisible;
    private Guid? _editingAccountId;
    private string _editorIssuer = string.Empty;
    private string _editorAccountName = string.Empty;
    private string _editorSecret = string.Empty;
    private int? _editorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
    private bool _editorIsFavorite;
    private bool _isAdvancedOptionsExpanded;
    private string _editorIssuerMessage = string.Empty;
    private string _editorSecretMessage = string.Empty;
    private string _editorPeriodMessage = string.Empty;
    private string _editorMessage = string.Empty;
    private IReadOnlyList<BrandIconOption> _editorBrandIconOptions = [];
    private BrandIconOption? _selectedEditorBrandIconOption;
    private bool _isGroupEditorVisible;
    private bool _isEditingFavorites;
    private Guid? _editingGroupId;
    private string _groupEditorName = string.Empty;
    private string _groupEditorMessage = string.Empty;
    private IReadOnlyList<GroupColorOption> _groupColorOptions = [];
    private GroupColorOption? _selectedGroupColor;
    private IReadOnlyList<GroupAccountSelectionViewModel> _allGroupEditorAccounts = [];
    private IReadOnlyList<GroupAccountSelectionViewModel> _groupEditorAccounts = [];
    private string _groupEditorSearchText = string.Empty;
    private bool _autoGenerateCodeOnSelection;

    public AccountListViewModel(
        IAccountManager accountManager,
        IAccountTotpService accountTotpService,
        IAsyncClipboardService clipboardService,
        IAccountQrCodeService accountQrCodeService,
        IAvaloniaQrImageFactory qrImageFactory,
        IAvaloniaDialogService dialogs,
        IAvaloniaLocalizationService localization,
        TimeSpan? countdownTickInterval = null,
        ISettingsService? settingsService = null,
        IAvaloniaQrPreviewDialogService? qrPreviewDialogs = null,
        TimeSpan? transientMessageDuration = null,
        IBrandIconResolver? brandIconResolver = null,
        IBrandIconPackService? brandIconPackService = null)
    {
        _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        _accountTotpService = accountTotpService ?? throw new ArgumentNullException(nameof(accountTotpService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _accountQrCodeService = accountQrCodeService ?? throw new ArgumentNullException(nameof(accountQrCodeService));
        _qrImageFactory = qrImageFactory ?? throw new ArgumentNullException(nameof(qrImageFactory));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _brandIconResolver = brandIconResolver ?? FallbackBrandIconResolver.Instance;
        _brandIconPackService = brandIconPackService;
        _brandIconResolver.CatalogChanged += BrandCatalogChanged;
        _countdownTickInterval = countdownTickInterval ?? TimeSpan.FromSeconds(1);
        _copyConfirmationDuration = transientMessageDuration
            ?? TransientNotificationDefaults.CopyConfirmationDuration;
        _settingsService = settingsService;
        _qrPreviewDialogs = qrPreviewDialogs;
        Notification = new NotificationState(transientMessageDuration);
        Notification.PropertyChanged += NotificationPropertyChanged;
        if (_countdownTickInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(countdownTickInterval));
        _loadCommand = new AsyncCommand(LoadAsync, () => !_isBusy);
        _generateCommand = new AsyncCommand(
            GenerateCodeAsync,
            () => !_isGenerating && _selectedAccount is not null);
        _copyCommand = new AsyncCommand(CopyCodeAsync, () => HasGeneratedCode);
        _copyAccountCodeCommand = new AsyncCommand<AccountListItemViewModel>(
            CopyAccountCodeAsync,
            _ => true);
        _toggleAccountFavoriteCommand = new AsyncCommand<AccountListItemViewModel>(
            ToggleAccountFavoriteAsync,
            _ => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible);
        _generateQrCommand = new AsyncCommand(GenerateQrAsync, () => _selectedAccount is not null);
        _beginAddCommand = new AsyncCommand(
            BeginAddAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible);
        _beginEditCommand = new AsyncCommand(
            BeginEditAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && SelectedAccount is not null);
        _saveAccountCommand = new AsyncCommand(SaveAccountAsync, () => !IsBusy && IsEditorVisible);
        _cancelEditCommand = new AsyncCommand(CancelEditAsync, () => !IsBusy && IsEditorVisible);
        _clearEditorPeriodCommand = new AsyncCommand(
            ClearEditorPeriodAsync,
            () => !IsBusy && IsEditorVisible && EditorPeriodSeconds.HasValue);
        _deleteAccountCommand = new AsyncCommand(
            DeleteAccountAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && SelectedAccount is not null);
        _beginContextEditCommand = new AsyncCommand(
            BeginContextEditAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && ContextAccount is not null);
        _generateContextQrCommand = new AsyncCommand(
            GenerateContextQrAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && ContextAccount is not null);
        _deleteContextAccountCommand = new AsyncCommand(
            DeleteContextAccountAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && ContextAccount is not null);
        _beginAddGroupCommand = new AsyncCommand(
            BeginAddGroupAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && _allAccounts.Count > 0);
        _beginEditFavoritesCommand = new AsyncCommand(
            BeginEditFavoritesAsync,
            () => !IsBusy && !IsEditorVisible && !IsGroupEditorVisible && _allAccounts.Count > 0);
        _saveGroupCommand = new AsyncCommand(
            SaveGroupAsync,
            () => !IsBusy && IsGroupEditorVisible);
        _cancelGroupEditCommand = new AsyncCommand(
            CancelGroupEditAsync,
            () => !IsBusy && IsGroupEditorVisible);
        _clearGroupFilterCommand = new AsyncCommand(
            ClearGroupFilterAsync,
            () => HasSelectedAccountNavigationCard);
        _toggleFavoritesFilterCommand = new AsyncCommand(
            ToggleFavoritesFilterAsync,
            () => !IsBusy && _allAccounts.Count > 0);
        _selectUngroupedCommand = new AsyncCommand(
            SelectUngroupedAsync,
            () => !IsBusy && HasUngroupedAccounts);
        _toggleAllAccountsFilterCommand = new AsyncCommand(
            ToggleAllAccountsFilterAsync,
            () => !IsBusy && _allAccounts.Count > 0);
        _localization.CultureChanged += LocalizationCultureChanged;
        RefreshSortOptions(AccountSortMode.Issuer);
        RefreshBrandIconOptions(null);
        RefreshGroupColorOptions(null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<AccountListItemViewModel>? AccountRevealRequested;

    public IReadOnlyList<AccountListItemViewModel> Accounts
    {
        get => _accounts;
        private set
        {
            if (!SetField(ref _accounts, value)) return;
            OnPropertyChanged(nameof(HasNoAccounts));
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(ShouldShowAccountNavigationCards));
            OnPropertyChanged(nameof(SearchResultSummary));
        }
    }

    public bool HasNoAccounts =>
        !IsBusy && !HasMessage && _allAccounts.Count == 0;

    public bool HasAnyAccounts => _allAccounts.Count > 0;

    public bool HasNoSearchResults =>
        !IsBusy
        && !HasMessage
        && HasActiveAccountFilter
        && _allAccounts.Count > 0
        && Accounts.Count == 0;

    public bool HasMultipleAccounts => _allAccounts.Count > 1;

    public IReadOnlyList<AccountSortOption> SortOptions
    {
        get => _sortOptions;
        private set => SetField(ref _sortOptions, value);
    }

    public AccountSortOption? SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (value is null || _selectedSortOption?.Mode == value.Mode) return;
            SortOptions = SortOptions
                .Select(option => option with { IsSelected = option.Mode == value.Mode })
                .ToArray();
            SetField(
                ref _selectedSortOption,
                SortOptions.First(option => option.Mode == value.Mode));
            OnPropertyChanged(nameof(IsIssuerSortSelected));
            OnPropertyChanged(nameof(IsIssuerDescendingSortSelected));
            OnPropertyChanged(nameof(IsAccountNameSortSelected));
            OnPropertyChanged(nameof(IsAccountNameDescendingSortSelected));
            if (_allAccounts.Count > 0)
                ApplyFilter();
        }
    }

    public bool IsIssuerSortSelected => SelectedSortOption?.Mode == AccountSortMode.Issuer;
    public bool IsIssuerDescendingSortSelected =>
        SelectedSortOption?.Mode == AccountSortMode.IssuerDescending;
    public bool IsAccountNameSortSelected =>
        SelectedSortOption?.Mode == AccountSortMode.AccountName;
    public bool IsAccountNameDescendingSortSelected =>
        SelectedSortOption?.Mode == AccountSortMode.AccountNameDescending;

    public Task SelectIssuerSortAsync() => SelectSortAsync(AccountSortMode.Issuer);
    public Task SelectIssuerDescendingSortAsync() =>
        SelectSortAsync(AccountSortMode.IssuerDescending);
    public Task SelectAccountNameSortAsync() => SelectSortAsync(AccountSortMode.AccountName);
    public Task SelectAccountNameDescendingSortAsync() =>
        SelectSortAsync(AccountSortMode.AccountNameDescending);

    public NotificationState Notification { get; }
    public string Message => Notification.Text;
    public bool HasMessage => Notification.HasMessage;

    public AccountListItemViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set => SetSelectedAccount(value, generateAndCopyCode: true);
    }

    public void SelectForKeyboardNavigation(AccountListItemViewModel account)
    {
        ArgumentNullException.ThrowIfNull(account);
        SetSelectedAccount(account, generateAndCopyCode: false);
        if (ReferenceEquals(_selectedAccount, account) && account.HasCode)
            ProjectSelectedCode(account);
    }

    public void EnableAutomaticCodeGenerationOnSelection() =>
        EnableAutomaticRowCodeGeneration();

    private void EnableAutomaticRowCodeGeneration()
    {
        _autoGenerateCodeOnSelection = true;
        if (_allAccounts.Count > 0)
            StartRowCodeLifetime(refreshImmediately: true);
    }

    public bool HasSelectedAccount => SelectedAccount is not null;

    private void SetSelectedAccount(
        AccountListItemViewModel? account,
        bool generateAndCopyCode)
    {
        if (!SetField(ref _selectedAccount, account)) return;
        ClearSelectedCodeProjection();
        ClearQrImage();
        _generateCommand.NotifyCanExecuteChanged();
        _generateQrCommand.NotifyCanExecuteChanged();
        _beginEditCommand.NotifyCanExecuteChanged();
        _deleteAccountCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedAccount));
        if (generateAndCopyCode && _autoGenerateCodeOnSelection && _selectedAccount is not null)
            _generateCommand.Execute(null);
    }

    public string GeneratedCode
    {
        get => _generatedCode;
        private set
        {
            if (!SetField(ref _generatedCode, value)) return;
            OnPropertyChanged(nameof(HasGeneratedCode));
            _copyCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasGeneratedCode => GeneratedCode.Length > 0;

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set => SetField(ref _remainingSeconds, Math.Max(0, value));
    }

    public int PeriodSeconds
    {
        get => _periodSeconds;
        private set => SetField(ref _periodSeconds, Math.Max(0, value));
    }

    public string CodeMessage
    {
        get => _codeMessage;
        private set
        {
            _codeMessageLocalizationKey = null;
            _codeMessageLocalizationArguments = [];
            if (!SetField(ref _codeMessage, value)) return;
            OnPropertyChanged(nameof(HasCodeMessage));
        }
    }

    public bool HasCodeMessage => CodeMessage.Length > 0;

    public IImage? QrImage => _qrImage?.Image;

    public bool HasQrImage => QrImage is not null;

    public double QrPreviewSize => 256 * Math.Clamp(
        _settingsService?.Current.QrPreviewScaleFactor
            ?? TOTP.Core.Models.AppSettings.DefaultQrPreviewScaleFactor,
        1.0,
        6.0);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty)) return;
            ClearRecentHighlight();
            OnPropertyChanged(nameof(HasSearchText));
            OnPropertyChanged(nameof(IsUngroupedFilterSelected));
            OnPropertyChanged(nameof(AccountNavigationBackLabel));
            if (HasSearchText && _selectedGroupId.HasValue)
            {
                _selectedGroupId = null;
                RefreshGroups();
                OnPropertyChanged(nameof(HasSelectedGroup));
                _clearGroupFilterCommand.NotifyCanExecuteChanged();
            }
            OnPropertyChanged(nameof(HasActiveAccountFilter));
            ApplyGroupSearch();
            ApplyFilter();
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public IReadOnlyList<AccountGroupListItemViewModel> Groups
    {
        get => _groups;
        private set
        {
            if (!SetField(ref _groups, value)) return;
            OnPropertyChanged(nameof(HasGroups));
            OnPropertyChanged(nameof(ShouldShowAccountNavigationCards));
        }
    }

    public bool HasGroups => _allGroups.Count > 0;

    public bool HasAccountNavigationCards => HasFavoriteAccounts || HasGroups;

    public bool ShouldShowAccountNavigationCards =>
        HasAccountNavigationCards && !HasNoSearchResults;

    public bool HasSelectedGroup => _selectedGroupId.HasValue;

    public int UngroupedCount => _allAccounts.Count(account => account.Group is null);

    public bool HasUngroupedAccounts => UngroupedCount > 0;

    public bool IsUngroupedFilterSelected => false;

    public int AllAccountCount => _allAccounts.Count;

    public bool IsAllAccountsFilterSelected => _showAllAccounts;

    public int FavoriteCount => _allAccounts.Count(account => account.IsFavorite);

    public bool HasFavoriteAccounts => FavoriteCount > 0;

    public string FavoriteGroupColor => NormalizeFavoriteGroupColor(
        _settingsService?.Current.FavoriteGroupColor);

    public IBrush FavoriteGroupBackground
    {
        get
        {
            var color = Color.Parse(FavoriteGroupColor);
            return new SolidColorBrush(Color.FromArgb(52, color.R, color.G, color.B));
        }
    }

    public IBrush FavoriteGroupForeground => new SolidColorBrush(Color.Parse(FavoriteGroupColor));

    public bool IsFavoritesFilterSelected => _showFavoritesOnly;

    public bool HasSelectedAccountNavigationCard =>
        HasSelectedGroup || IsFavoritesFilterSelected;

    public bool HasActiveAccountFilter =>
        HasSearchText || HasSelectedGroup || IsFavoritesFilterSelected;

    public string AccountNavigationBackLabel =>
        _localization.GetString(AvaloniaStringKeys.AllAccounts);

    public string SearchResultSummary => string.Format(
        _localization.GetString(AvaloniaStringKeys.SearchResultsFormat),
        Accounts.Count,
        _allAccounts.Count);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            _loadCommand.NotifyCanExecuteChanged();
            _toggleFavoritesFilterCommand.NotifyCanExecuteChanged();
            _selectUngroupedCommand.NotifyCanExecuteChanged();
            _toggleAllAccountsFilterCommand.NotifyCanExecuteChanged();
            NotifyCrudCommands();
            OnPropertyChanged(nameof(HasNoAccounts));
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(ShouldShowAccountNavigationCards));
        }
    }

    public ICommand LoadCommand => _loadCommand;

    public ICommand GenerateCommand => _generateCommand;

    public ICommand CopyCommand => _copyCommand;

    public ICommand CopyAccountCodeCommand => _copyAccountCodeCommand;

    public ICommand GenerateQrCommand => _generateQrCommand;

    public ICommand BeginAddCommand => _beginAddCommand;
    public ICommand BeginEditCommand => _beginEditCommand;
    public ICommand SaveAccountCommand => _saveAccountCommand;
    public ICommand CancelEditCommand => _cancelEditCommand;
    public ICommand ClearEditorPeriodCommand => _clearEditorPeriodCommand;
    public ICommand DeleteAccountCommand => _deleteAccountCommand;
    public ICommand BeginContextEditCommand => _beginContextEditCommand;
    public ICommand GenerateContextQrCommand => _generateContextQrCommand;
    public ICommand DeleteContextAccountCommand => _deleteContextAccountCommand;
    public ICommand BeginAddGroupCommand => _beginAddGroupCommand;
    public ICommand BeginEditFavoritesCommand => _beginEditFavoritesCommand;
    public ICommand SaveGroupCommand => _saveGroupCommand;
    public ICommand CancelGroupEditCommand => _cancelGroupEditCommand;
    public ICommand ClearGroupFilterCommand => _clearGroupFilterCommand;
    public ICommand ToggleFavoritesFilterCommand => _toggleFavoritesFilterCommand;
    public ICommand SelectUngroupedCommand => _selectUngroupedCommand;
    public ICommand ToggleAllAccountsFilterCommand => _toggleAllAccountsFilterCommand;

    public bool IsGroupEditorVisible
    {
        get => _isGroupEditorVisible;
        private set
        {
            if (!SetField(ref _isGroupEditorVisible, value)) return;
            NotifyCrudCommands();
            _beginAddGroupCommand.NotifyCanExecuteChanged();
            _saveGroupCommand.NotifyCanExecuteChanged();
            _cancelGroupEditCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsEditingExistingGroup => _editingGroupId.HasValue;

    public bool IsEditingFavorites => _isEditingFavorites;

    public bool IsCreatingGroup => !_isEditingFavorites && !_editingGroupId.HasValue;

    public string GroupEditorName
    {
        get => _groupEditorName;
        set
        {
            if (!SetField(ref _groupEditorName, value ?? string.Empty)) return;
            GroupEditorMessage = string.Empty;
        }
    }

    public string GroupEditorMessage
    {
        get => _groupEditorMessage;
        private set => SetField(ref _groupEditorMessage, value);
    }

    public IReadOnlyList<GroupColorOption> GroupColorOptions
    {
        get => _groupColorOptions;
        private set => SetField(ref _groupColorOptions, value);
    }

    public GroupColorOption? SelectedGroupColor
    {
        get => _selectedGroupColor;
        set => SetField(ref _selectedGroupColor, value);
    }

    public IReadOnlyList<GroupAccountSelectionViewModel> GroupEditorAccounts
    {
        get => _groupEditorAccounts;
        private set => SetField(ref _groupEditorAccounts, value);
    }

    public string GroupEditorSearchText
    {
        get => _groupEditorSearchText;
        set
        {
            if (!SetField(ref _groupEditorSearchText, value ?? string.Empty)) return;
            ApplyGroupEditorSearch();
        }
    }

    public AccountListItemViewModel? ContextAccount
    {
        get => _contextAccount;
        set
        {
            if (!SetField(ref _contextAccount, value)) return;
            _beginContextEditCommand.NotifyCanExecuteChanged();
            _generateContextQrCommand.NotifyCanExecuteChanged();
            _deleteContextAccountCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsEditorVisible
    {
        get => _isEditorVisible;
        private set
        {
            if (!SetField(ref _isEditorVisible, value)) return;
            NotifyCrudCommands();
        }
    }

    public bool IsEditingExistingAccount => _editingAccountId.HasValue;

    public string EditorIssuer
    {
        get => _editorIssuer;
        set
        {
            if (!SetField(ref _editorIssuer, value ?? string.Empty)) return;
            EditorIssuerMessage = string.Empty;
            EditorMessage = string.Empty;
        }
    }

    public string EditorAccountName
    {
        get => _editorAccountName;
        set
        {
            if (!SetField(ref _editorAccountName, value ?? string.Empty)) return;
            EditorMessage = string.Empty;
        }
    }

    public string EditorSecret
    {
        get => _editorSecret;
        set
        {
            if (!SetField(ref _editorSecret, value ?? string.Empty)) return;
            EditorSecretMessage = string.Empty;
            EditorMessage = string.Empty;
        }
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

    public string EditorMessage
    {
        get => _editorMessage;
        private set => SetField(ref _editorMessage, value);
    }

    public bool EditorIsFavorite
    {
        get => _editorIsFavorite;
        set => SetField(ref _editorIsFavorite, value);
    }

    public IReadOnlyList<BrandIconOption> EditorBrandIconOptions
    {
        get => _editorBrandIconOptions;
        private set
        {
            if (!SetField(ref _editorBrandIconOptions, value)) return;
            OnPropertyChanged(nameof(HasBrandIconChoices));
        }
    }

    public BrandIconOption? SelectedEditorBrandIconOption
    {
        get => _selectedEditorBrandIconOption;
        set => SetField(ref _selectedEditorBrandIconOption, value);
    }

    public bool HasBrandIconChoices => EditorBrandIconOptions.Count > 1;

    public Task LoadAsync() => LoadAsync(null);

#if DEBUG
    private const string DebugSyntheticAccountMarker = "otp-harbor-debug-load-test:";

    public async Task<bool> AddDebugSyntheticAccountsAsync(int count = 600)
    {
        if (IsBusy || count is < 1 or > 5000) return false;

        IsBusy = true;
        StopAndClearRowCodes();
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (loaded.IsFailed) return false;

            var accounts = loaded.Value
                .Where(account => !IsDebugSyntheticAccount(account))
                .ToList();
            var groups = new[]
            {
                new AccountGroup(new Guid("20000000-0000-0000-0000-000000000001"), "Desktop Load Test 1", "#4C956C"),
                new AccountGroup(new Guid("20000000-0000-0000-0000-000000000002"), "Desktop Load Test 2", "#18A999"),
                new AccountGroup(new Guid("20000000-0000-0000-0000-000000000003"), "Desktop Load Test 3", "#4F6BED"),
                new AccountGroup(new Guid("20000000-0000-0000-0000-000000000004"), "Desktop Load Test 4", "#F59E0B"),
                new AccountGroup(new Guid("20000000-0000-0000-0000-000000000005"), "Desktop Load Test 5", "#E45757"),
                new AccountGroup(new Guid("20000000-0000-0000-0000-000000000006"), "Desktop Load Test 6", "#B455C7")
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

            IsBusy = false;
            await LoadAsync();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DeleteDebugSyntheticAccountsAsync()
    {
        if (IsBusy) return false;

        IsBusy = true;
        StopAndClearRowCodes();
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (loaded.IsFailed) return false;
            var retained = loaded.Value
                .Where(account => !IsDebugSyntheticAccount(account))
                .ToArray();
            var saved = await _accountManager.CommitImportAsync(retained);
            if (saved.IsFailed) return false;

            IsBusy = false;
            await LoadAsync();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsDebugSyntheticAccount(Account account) =>
        account.AccountName?.StartsWith(
            DebugSyntheticAccountMarker,
            StringComparison.Ordinal) == true;
#endif

    private async Task LoadAsync(Guid? recentlyAddedAccountId)
    {
        if (IsBusy) return;

        IsBusy = true;
        StopAndClearRowCodes();
        ClearNotification();
        try
        {
            var result = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (result.IsFailed)
            {
                _allAccounts = [];
                RefreshFavoriteState();
                RefreshGroups();
                Accounts = [];
                ShowError(_localization.GetString(AvaloniaStringKeys.AccountsLoadFailed));
                return;
            }

            SelectedAccount = null;
            _allAccounts = result.Value
                .Select(account =>
                {
                    AccountGroupPolicy.TryNormalizeStored(account.Group, out var group);
                    return new AccountListItemViewModel(
                        account.ID,
                        account.Issuer,
                        account.AccountName ?? string.Empty,
                        account.ID == recentlyAddedAccountId,
                        _copyAccountCodeCommand,
                        account.PeriodSeconds,
                        FormatCustomPeriod(account.PeriodSeconds),
                        _brandIconResolver.ResolveAccount(
                            account.ID,
                            account.Issuer,
                            account.AccountName),
                        group,
                        account.IsFavorite,
                        _toggleAccountFavoriteCommand);
                })
                .ToArray();
            RefreshFavoriteState();
            foreach (var account in _allAccounts)
                account.UpdateLogoVisibility(_brandIconResolver.ShowIssuerLogo);
            RefreshGroups();
            ApplyFilter();
            StartRecentHighlightLifetime(
                _allAccounts.FirstOrDefault(account => account.IsRecentlyAdded));
            if (_autoGenerateCodeOnSelection)
                StartRowCodeLifetime(refreshImmediately: true);
        }
        catch (Exception)
        {
            _allAccounts = [];
            RefreshFavoriteState();
            RefreshGroups();
            Accounts = [];
            ShowError(_localization.GetString(AvaloniaStringKeys.AccountsLoadFailedSafely));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task GenerateCodeAsync()
    {
        var requestedAccount = _selectedAccount;
        if (requestedAccount is null || _isGenerating) return;

        if (requestedAccount.HasCode)
        {
            ProjectSelectedCode(requestedAccount);
            if (_autoGenerateCodeOnSelection)
                await CopyAccountCodeAsync(requestedAccount);
            return;
        }

        _isGenerating = true;
        _generateCommand.NotifyCanExecuteChanged();
        ClearSelectedCodeProjection();
        try
        {
            var result = await _accountTotpService.GenerateAsync(requestedAccount.Id);
            if (_selectedAccount?.Id != requestedAccount.Id)
                return;
            if (result.IsFailed)
            {
                SetLocalizedCodeMessage(AvaloniaStringKeys.CodeGenerationFailed);
                return;
            }

            requestedAccount.UpdateCode(
                result.Value.Code,
                result.Value.RemainingSeconds,
                result.Value.PeriodSeconds);
            ProjectSelectedCode(requestedAccount);
            StartRowCodeLifetime(refreshImmediately: false);
            if (_autoGenerateCodeOnSelection
                && _selectedAccount?.Id == requestedAccount.Id)
            {
                await CopyAccountCodeAsync(requestedAccount);
            }
        }
        catch (Exception)
        {
            if (_selectedAccount?.Id == requestedAccount.Id)
                SetLocalizedCodeMessage(AvaloniaStringKeys.CodeGenerationFailedSafely);
        }
        finally
        {
            _isGenerating = false;
            _generateCommand.NotifyCanExecuteChanged();
            if (_autoGenerateCodeOnSelection
                && _selectedAccount is not null
                && _selectedAccount.Id != requestedAccount.Id
                && !HasGeneratedCode)
            {
                _generateCommand.Execute(null);
            }
        }
    }

    public async Task CopyCodeAsync()
    {
        if (_selectedAccount is not { HasCode: true } account) return;
        await CopyAccountCodeAsync(account);
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
            EditorMessage = string.Empty;
        }
    }

    public bool HasEditorPeriodSeconds => EditorPeriodSeconds.HasValue;

    public Task ClearEditorPeriodAsync()
    {
        EditorPeriodSeconds = null;
        return Task.CompletedTask;
    }

    public bool IsAdvancedOptionsExpanded
    {
        get => _isAdvancedOptionsExpanded;
        set => SetField(ref _isAdvancedOptionsExpanded, value);
    }

    public async Task CopyAccountCodeAsync(AccountListItemViewModel account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!account.HasCode || !_allAccounts.Contains(account) && !ReferenceEquals(account, _selectedAccount))
            return;

        var code = account.Code;
        var remainingSeconds = Math.Max(1, account.RemainingSeconds);

        var configuredLifetime = _settingsService?.Current.ClearClipboardSeconds
            ?? remainingSeconds;
        var clearSeconds = Math.Min(
            remainingSeconds,
            TOTP.Core.Validation.ClipboardLifetimePolicy.NormalizeSeconds(configuredLifetime));
        var clearResult = await _clipboardService.CopyAndScheduleClearAsync(
            code,
            TimeSpan.FromSeconds(clearSeconds));
        if (clearResult.IsSuccess)
        {
            ShowCopyConfirmation(account);
            return;
        }

        var requiredCapabilities =
            ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear;
        if ((_clipboardService.Capabilities & requiredCapabilities) == ClipboardCapabilities.WriteText)
        {
            var fallbackResult = await _clipboardService.CopyAsync(code);
            if (fallbackResult.IsSuccess)
            {
                ShowCopyConfirmation(account);
                ShowLocalizedTransientNotification(
                    AvaloniaStringKeys.CodeCopiedWithoutClear,
                    NotificationSeverity.Warning);
            }
            else
            {
                ShowLocalizedTransientNotification(
                    AvaloniaStringKeys.ClipboardCopyUnavailable,
                    NotificationSeverity.Error);
            }

            return;
        }

        ShowLocalizedTransientNotification(
            AvaloniaStringKeys.ClipboardCopyUnavailable,
            NotificationSeverity.Error);
    }

    public async Task ToggleAccountFavoriteAsync(AccountListItemViewModel account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (IsBusy
            || _isFavoriteUpdateInProgress
            || IsEditorVisible
            || IsGroupEditorVisible
            || !_allAccounts.Contains(account))
            return;

        var makeFavorite = !account.IsFavorite;
        _isFavoriteUpdateInProgress = true;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            var existing = loaded.IsSuccess
                ? loaded.Value.FirstOrDefault(value => value.ID == account.Id)
                : null;
            if (existing is null)
            {
                ShowLocalizedTransientNotification(
                    AvaloniaStringKeys.FavoriteUpdateFailed,
                    NotificationSeverity.Error);
                return;
            }

            var saved = await _accountManager.UpdateAsync(
                existing,
                existing.WithFavorite(makeFavorite));
            if (saved.IsFailed)
            {
                ShowLocalizedTransientNotification(
                    AvaloniaStringKeys.FavoriteUpdateFailed,
                    NotificationSeverity.Error);
                return;
            }

            var hadFavoriteAccounts = HasFavoriteAccounts;
            var wasFilteringFavorites = _showFavoritesOnly;
            account.UpdateFavorite(makeFavorite);
            RefreshFavoriteState(hadFavoriteAccounts != HasFavoriteAccounts);
            if (wasFilteringFavorites)
                ApplyFilter();
        }
        catch (Exception)
        {
            ShowLocalizedTransientNotification(
                AvaloniaStringKeys.FavoriteUpdateFailed,
                NotificationSeverity.Error);
        }
        finally
        {
            _isFavoriteUpdateInProgress = false;
        }
    }

    public Task GenerateQrAsync() => GenerateQrAsync(_selectedAccount);

    public Task GenerateContextQrAsync() => GenerateQrAsync(ContextAccount);

    private async Task GenerateQrAsync(AccountListItemViewModel? account)
    {
        if (account is null) return;

        var accountId = account.Id;
        var issuer = account.Issuer;
        var accountName = account.AccountName;
        ClearQrImage();
        var result = await _accountQrCodeService.GenerateAsync(accountId);
        if (result.IsFailed)
        {
            SetLocalizedCodeMessage(AvaloniaStringKeys.QrGenerationFailed);
            return;
        }

        using var sensitivePng = result.Value;
        try
        {
            _qrImage = _qrImageFactory.Create(sensitivePng.Memory);
            OnPropertyChanged(nameof(QrImage));
            OnPropertyChanged(nameof(HasQrImage));
            await ShowQrPreviewAsync(issuer, accountName);
        }
        catch (Exception)
        {
            SetLocalizedCodeMessage(AvaloniaStringKeys.QrDisplayFailed);
        }
        finally
        {
            ClearQrImage();
        }
    }

    private async Task ShowQrPreviewAsync(string issuer, string accountName)
    {
        var image = QrImage;
        if (image is null) return;
        if (_qrPreviewDialogs is null)
        {
            SetLocalizedCodeMessage(AvaloniaStringKeys.QrPreviewUnavailable);
            return;
        }

        try
        {
            var title = string.Format(
                _localization.GetString(AvaloniaStringKeys.GeneratedQrTitleFormat),
                issuer,
                accountName);
            await _qrPreviewDialogs.ShowAsync(image, title, QrPreviewSize);
        }
        catch (Exception)
        {
            if (ReferenceEquals(QrImage, image))
                SetLocalizedCodeMessage(AvaloniaStringKeys.QrPreviewDisplayFailed);
        }
    }

    public Task BeginAddAsync()
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible) return Task.CompletedTask;
        ClearEditor();
        IsEditorVisible = true;
        return Task.CompletedTask;
    }

    public Task BeginEditAsync() => BeginEditAsync(SelectedAccount);

    public Task BeginContextEditAsync() => BeginEditAsync(ContextAccount);

    private async Task BeginEditAsync(AccountListItemViewModel? target)
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible || target is null) return;

        IsBusy = true;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            var account = loaded.IsSuccess
                ? loaded.Value.FirstOrDefault(value => value.ID == target.Id)
                : null;
            if (account is null)
            {
                ShowError(_localization.GetString(AvaloniaStringKeys.AccountEditLoadFailed));
                return;
            }

            _editingAccountId = account.ID;
            OnPropertyChanged(nameof(IsEditingExistingAccount));
            EditorIssuer = account.Issuer;
            EditorAccountName = account.AccountName ?? string.Empty;
            EditorSecret = account.Secret;
            EditorPeriodSeconds = account.PeriodSeconds;
            EditorIsFavorite = account.IsFavorite;
            RefreshBrandIconOptions(_brandIconPackService?.GetAccountBrandId(account.ID));
            IsEditorVisible = true;
        }
        catch (Exception)
        {
            ShowError(_localization.GetString(AvaloniaStringKeys.AccountEditLoadFailed));
            ClearEditor();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAccountAsync()
    {
        if (IsBusy || !IsEditorVisible) return;

        var issuer = EditorIssuer.Trim();
        var accountName = EditorAccountName.Trim();
        var secret = EditorSecret;
        EditorSecret = string.Empty;
        if (issuer.Length == 0)
        {
            EditorIssuerMessage = _localization.GetString(AvaloniaStringKeys.AccountIssuerRequired);
            return;
        }

        if (!SecretValidation.IsValidBase32Secret(secret))
        {
            EditorSecretMessage = _localization.GetString(AvaloniaStringKeys.AccountSecretInvalid);
            return;
        }
        if (EditorPeriodSeconds is not int periodSeconds
            || !TotpPeriodPolicy.IsSupported(periodSeconds))
        {
            IsAdvancedOptionsExpanded = true;
            EditorPeriodMessage = _localization.GetString(AvaloniaStringKeys.TotpPeriodInvalid);
            return;
        }

        IsBusy = true;
        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            if (loaded.IsFailed)
            {
                EditorMessage = _localization.GetString(AvaloniaStringKeys.AccountSaveFailed);
                return;
            }

            if (HasDuplicateIdentity(loaded.Value, issuer, accountName, _editingAccountId))
            {
                EditorMessage = _localization.GetString(AvaloniaStringKeys.AccountDuplicate);
                return;
            }

            var isNewAccount = !_editingAccountId.HasValue;
            var selectedBrandId = SelectedEditorBrandIconOption?.Id;
            var selectedGroup = isNewAccount
                ? _allGroups.FirstOrDefault(group => group.Id == _selectedGroupId)?.Group
                : null;
            var normalizedSecret = SecretValidation.NormalizeBase32Secret(secret);
            var updated = new Account(
                _editingAccountId ?? Guid.NewGuid(),
                issuer,
                normalizedSecret,
                accountName.Length == 0 ? null : accountName,
                periodSeconds,
                selectedGroup,
                EditorIsFavorite);
            var saved = _editingAccountId.HasValue
                ? await UpdateExistingAsync(loaded.Value, updated)
                : await _accountManager.AddNewAsync(updated);
            if (saved.IsFailed)
            {
                EditorMessage = _localization.GetString(AvaloniaStringKeys.AccountSaveFailed);
                return;
            }

            var iconPreferenceSaved = true;
            if (_brandIconPackService is not null)
            {
                try
                {
                    iconPreferenceSaved = (await _brandIconPackService.SetAccountBrandIdAsync(
                        updated.ID,
                        selectedBrandId)).IsSuccess;
                }
                catch (Exception)
                {
                    iconPreferenceSaved = false;
                }
            }

            ClearEditor();
            IsBusy = false;
            await LoadAsync(isNewAccount ? updated.ID : null);
            if (!HasMessage)
            {
                SetSelectedAccount(
                    Accounts.FirstOrDefault(account => account.Id == updated.ID),
                    generateAndCopyCode: false);
                ShowTransientMessage(_localization.GetString(
                    iconPreferenceSaved
                        ? AvaloniaStringKeys.AccountSaved
                        : AvaloniaStringKeys.AccountSavedIconPreferenceFailed));
            }
        }
        catch (Exception)
        {
            EditorMessage = _localization.GetString(AvaloniaStringKeys.AccountSaveFailed);
        }
        finally
        {
            secret = string.Empty;
            IsBusy = false;
        }
    }

    public Task CancelEditAsync()
    {
        if (IsBusy || !IsEditorVisible) return Task.CompletedTask;
        ClearEditor();
        return Task.CompletedTask;
    }

    public async Task RevealImportedAccountAsync(
        Guid accountId,
        bool highlightAsNew,
        string successMessage)
    {
        SearchText = string.Empty;
        await LoadAsync(highlightAsNew ? accountId : null);
        if (HasMessage) return;

        var revealed = _allAccounts.FirstOrDefault(account => account.Id == accountId);
        if (revealed is not null && Accounts.All(account => account.Id != accountId))
        {
            _showFavoritesOnly = false;
            _showAllAccounts = false;
            _selectedGroupId = revealed.Group?.Id;
            RefreshFavoriteState();
            RefreshGroups();
            ApplyFilter();
        }
        var accountToReveal = Accounts.FirstOrDefault(account => account.Id == accountId);
        if (accountToReveal is not null)
            AccountRevealRequested?.Invoke(accountToReveal);
        ShowTransientMessage(successMessage);
    }

    public void ShowQrImportOutcome(string message, NotificationSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ClearNotificationLocalization();
        Notification.ShowTransient(message, severity);
    }

    public Task DeleteAccountAsync() => DeleteAccountAsync(SelectedAccount);

    public Task DeleteContextAccountAsync() => DeleteAccountAsync(ContextAccount);

    private async Task DeleteAccountAsync(AccountListItemViewModel? target)
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible || target is null) return;

        var selected = target;
        IsBusy = true;
        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(new ConfirmationDialogRequest(
                _localization.GetString(AvaloniaStringKeys.DeleteAccount),
                string.Format(
                    _localization.GetString(AvaloniaStringKeys.DeleteAccountPrompt),
                    selected.Issuer,
                    selected.AccountName),
                NotificationSeverity.Warning,
                _localization.GetString(AvaloniaStringKeys.Delete),
                _localization.GetString(AvaloniaStringKeys.Cancel),
                IsDestructive: true));
        }
        catch (Exception)
        {
            ShowError(_localization.GetString(AvaloniaStringKeys.AccountDeleteFailed));
            IsBusy = false;
            return;
        }
        if (!confirmed)
        {
            IsBusy = false;
            return;
        }

        try
        {
            var loaded = await _accountManager.GetAllOtpEntriesSortedAsync();
            var account = loaded.IsSuccess
                ? loaded.Value.FirstOrDefault(value => value.ID == selected.Id)
                : null;
            if (account is null || (await _accountManager.DeleteAsync(account)).IsFailed)
            {
                ShowError(_localization.GetString(AvaloniaStringKeys.AccountDeleteFailed));
                return;
            }

            if (_brandIconPackService is not null)
            {
                try
                {
                    await _brandIconPackService.SetAccountBrandIdAsync(selected.Id, null);
                }
                catch (Exception)
                {
                    // Account deletion is authoritative; a stale local display preference is harmless.
                }
            }

            SelectedAccount = null;
            IsBusy = false;
            await LoadAsync();
            if (!HasMessage)
                ShowTransientMessage(_localization.GetString(AvaloniaStringKeys.AccountDeleted));
        }
        catch (Exception)
        {
            ShowError(_localization.GetString(AvaloniaStringKeys.AccountDeleteFailed));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<FluentResults.Result> UpdateExistingAsync(
        IReadOnlyList<Account> accounts,
        Account updated)
    {
        var previous = accounts.FirstOrDefault(value => value.ID == updated.ID);
        return previous is null
            ? FluentResults.Result.Fail("Account unavailable for update.")
            : await _accountManager.UpdateAsync(previous, updated);
    }

    private static bool HasDuplicateIdentity(
        IEnumerable<Account> accounts,
        string issuer,
        string accountName,
        Guid? excludedId) =>
        accounts.Any(account => account.ID != excludedId
            && string.Equals(account.Issuer.Trim(), issuer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                (account.AccountName ?? string.Empty).Trim(),
                accountName,
                StringComparison.OrdinalIgnoreCase));

    public Task BeginAddGroupAsync()
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible || _allAccounts.Count == 0)
            return Task.CompletedTask;

        SetGroupEditorMode(null, isEditingFavorites: false);
        GroupEditorName = string.Empty;
        GroupEditorMessage = string.Empty;
        RefreshGroupColorOptions(null);
        GroupEditorSearchText = string.Empty;
        _allGroupEditorAccounts = CreateGroupAccountSelections(null);
        ApplyGroupEditorSearch();
        IsGroupEditorVisible = true;
        return Task.CompletedTask;
    }

    private Task BeginEditGroupAsync(Guid groupId)
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible)
            return Task.CompletedTask;

        var group = _allGroups.FirstOrDefault(item => item.Id == groupId)?.Group;
        if (group is null) return Task.CompletedTask;

        SetGroupEditorMode(group.Id, isEditingFavorites: false);
        GroupEditorName = group.Name;
        GroupEditorMessage = string.Empty;
        RefreshGroupColorOptions(group.Color);
        GroupEditorSearchText = string.Empty;
        _allGroupEditorAccounts = CreateGroupAccountSelections(group.Id);
        ApplyGroupEditorSearch();
        IsGroupEditorVisible = true;
        return Task.CompletedTask;
    }

    public Task BeginEditFavoritesAsync()
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible || _allAccounts.Count == 0)
            return Task.CompletedTask;

        SetGroupEditorMode(null, isEditingFavorites: true);
        GroupEditorName = _localization.GetString(AvaloniaStringKeys.FavoriteAccounts);
        GroupEditorMessage = string.Empty;
        RefreshGroupColorOptions(FavoriteGroupColor);
        GroupEditorSearchText = string.Empty;
        _allGroupEditorAccounts = CreateFavoriteAccountSelections();
        ApplyGroupEditorSearch();
        IsGroupEditorVisible = true;
        return Task.CompletedTask;
    }

    public async Task SaveGroupAsync()
    {
        if (IsBusy || !IsGroupEditorVisible) return;

        var name = GroupEditorName.Trim();
        if (!IsEditingFavorites && name.Length == 0)
        {
            GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.GroupNameRequired);
            return;
        }

        if (!IsEditingFavorites
            && _allGroups.Any(group => group.Id != _editingGroupId
                && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.GroupNameDuplicate);
            return;
        }

        var selectedAccountIds = _allGroupEditorAccounts
            .Where(account => account.IsSelected)
            .Select(account => account.AccountId)
            .ToArray();
        if (selectedAccountIds.Length == 0)
        {
            GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.GroupAccountRequired);
            return;
        }

        var color = SelectedGroupColor ?? GroupColorOptions.First();
        if (IsEditingFavorites)
        {
            await SaveFavoritesAsync(selectedAccountIds, color);
            return;
        }

        var group = new AccountGroup(_editingGroupId ?? Guid.NewGuid(), name, color.Hex);
        IsBusy = true;
        try
        {
            var result = await _accountManager.SaveGroupAsync(group, selectedAccountIds);
            if (result.IsFailed)
            {
                GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.GroupSaveFailed);
                return;
            }

            _selectedGroupId = group.Id;
            _showAllAccounts = false;
            ClearGroupEditor();
            IsBusy = false;
            await LoadAsync();
            ShowLocalizedTransientNotification(
                AvaloniaStringKeys.GroupSaved,
                NotificationSeverity.Success);
        }
        catch (Exception)
        {
            GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.GroupSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveFavoritesAsync(
        IReadOnlyCollection<Guid> selectedAccountIds,
        GroupColorOption color)
    {
        IsBusy = true;
        try
        {
            var result = await _accountManager.SaveFavoritesAsync(selectedAccountIds);
            if (result.IsFailed)
            {
                GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.FavoritesSaveFailed);
                return;
            }

            var colorSaved = true;
            if (_settingsService is not null)
            {
                var previousColor = _settingsService.Current.FavoriteGroupColor;
                _settingsService.Current.FavoriteGroupColor = color.Hex;
                var settingsResult = await _settingsService.SaveAsync();
                if (settingsResult.IsFailed)
                {
                    _settingsService.Current.FavoriteGroupColor = previousColor;
                    colorSaved = false;
                }
            }

            _showFavoritesOnly = true;
            _showAllAccounts = false;
            ClearGroupEditor();
            IsBusy = false;
            await LoadAsync();
            OnPropertyChanged(nameof(FavoriteGroupColor));
            OnPropertyChanged(nameof(FavoriteGroupBackground));
            OnPropertyChanged(nameof(FavoriteGroupForeground));
            ShowLocalizedTransientNotification(
                colorSaved
                    ? AvaloniaStringKeys.FavoritesSaved
                    : AvaloniaStringKeys.FavoriteColorSaveFailed,
                colorSaved ? NotificationSeverity.Success : NotificationSeverity.Error);
        }
        catch (Exception)
        {
            GroupEditorMessage = _localization.GetString(AvaloniaStringKeys.FavoritesSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CancelGroupEditAsync()
    {
        if (IsBusy || !IsGroupEditorVisible) return Task.CompletedTask;
        ClearGroupEditor();
        return Task.CompletedTask;
    }

    private async Task DeleteGroupAsync(Guid groupId, string groupName)
    {
        if (IsBusy || IsEditorVisible || IsGroupEditorVisible) return;
        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(new ConfirmationDialogRequest(
                _localization.GetString(AvaloniaStringKeys.DeleteGroup),
                string.Format(
                    _localization.GetString(AvaloniaStringKeys.DeleteGroupPrompt),
                    groupName),
                NotificationSeverity.Warning,
                _localization.GetString(AvaloniaStringKeys.Delete),
                _localization.GetString(AvaloniaStringKeys.Cancel),
                IsDestructive: true));
        }
        catch (Exception)
        {
            ShowError(_localization.GetString(AvaloniaStringKeys.GroupDeleteFailed));
            return;
        }

        if (!confirmed) return;
        IsBusy = true;
        try
        {
            var result = await _accountManager.DeleteGroupAsync(groupId);
            if (result.IsFailed)
            {
                ShowError(_localization.GetString(AvaloniaStringKeys.GroupDeleteFailed));
                return;
            }

            if (_selectedGroupId == groupId) _selectedGroupId = null;
            IsBusy = false;
            await LoadAsync();
            ShowLocalizedTransientNotification(
                AvaloniaStringKeys.GroupDeleted,
                NotificationSeverity.Success);
        }
        catch (Exception)
        {
            ShowError(_localization.GetString(AvaloniaStringKeys.GroupDeleteFailed));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task SelectGroupAsync(Guid groupId)
    {
        _selectedGroupId = _selectedGroupId == groupId ? null : groupId;
        _showFavoritesOnly = false;
        _showAllAccounts = false;
        ClearRecentHighlight();
        RefreshGroups();
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyFilter();
        return Task.CompletedTask;
    }

    private Task ToggleFavoritesFilterAsync()
    {
        if (IsBusy || _allAccounts.Count == 0) return Task.CompletedTask;
        _showFavoritesOnly = !_showFavoritesOnly;
        _selectedGroupId = null;
        _showAllAccounts = false;
        ClearRecentHighlight();
        RefreshGroups();
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(IsUngroupedFilterSelected));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyFilter();
        return Task.CompletedTask;
    }

    private Task ClearGroupFilterAsync()
    {
        if (!HasSelectedAccountNavigationCard) return Task.CompletedTask;
        _selectedGroupId = null;
        _showFavoritesOnly = false;
        _showAllAccounts = false;
        RefreshGroups();
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(IsUngroupedFilterSelected));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyFilter();
        return Task.CompletedTask;
    }

    private Task SelectUngroupedAsync()
    {
        if (IsBusy || !HasUngroupedAccounts) return Task.CompletedTask;

        _selectedGroupId = null;
        _showFavoritesOnly = false;
        _showAllAccounts = false;
        ClearRecentHighlight();
        if (HasSearchText)
        {
            SearchText = string.Empty;
            return Task.CompletedTask;
        }

        RefreshGroups();
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(IsUngroupedFilterSelected));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyFilter();
        return Task.CompletedTask;
    }

    private Task ToggleAllAccountsFilterAsync()
    {
        if (IsBusy || _allAccounts.Count == 0) return Task.CompletedTask;

        _showAllAccounts = !_showAllAccounts;
        _selectedGroupId = null;
        _showFavoritesOnly = false;
        ClearRecentHighlight();
        RefreshGroups();
        OnPropertyChanged(nameof(IsAllAccountsFilterSelected));
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(IsUngroupedFilterSelected));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        ApplyFilter();
        return Task.CompletedTask;
    }

    private IReadOnlyList<GroupAccountSelectionViewModel> CreateGroupAccountSelections(Guid? groupId) =>
        _allAccounts
            .Select(account => new GroupAccountSelectionViewModel(
                account.Id,
                account.Issuer,
                account.AccountName,
                account.Brand,
                account.ShowIssuerLogo,
                account.Group?.Id == groupId && groupId.HasValue))
            .ToArray();

    private IReadOnlyList<GroupAccountSelectionViewModel> CreateFavoriteAccountSelections() =>
        _allAccounts
            .Select(account => new GroupAccountSelectionViewModel(
                account.Id,
                account.Issuer,
                account.AccountName,
                account.Brand,
                account.ShowIssuerLogo,
                account.IsFavorite))
            .ToArray();

    private void ApplyGroupEditorSearch()
    {
        var terms = GroupEditorSearchText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        GroupEditorAccounts = terms.Length == 0
            ? _allGroupEditorAccounts
            : _allGroupEditorAccounts
                .Where(account => terms.All(term =>
                    account.Issuer.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || account.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
    }

    private void ApplyFilter()
    {
        var filteredAccounts = AccountListFilter.Apply(
            _allAccounts,
            SearchText,
            _selectedGroupId,
            _showFavoritesOnly,
            _showAllAccounts);
        Accounts = SortAccounts(filteredAccounts);
        var selectionIsVisible = SelectedAccount is not null
            && Accounts.Any(account => account.Id == SelectedAccount.Id);
        if (selectionIsVisible) return;

        SelectedAccount = null;
    }

    private IReadOnlyList<AccountListItemViewModel> SortAccounts(
        IReadOnlyList<AccountListItemViewModel> accounts)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        return (SelectedSortOption?.Mode ?? AccountSortMode.Issuer) switch
        {
            AccountSortMode.IssuerDescending => accounts
                .OrderByDescending(account => account.Issuer, comparer)
                .ThenByDescending(account => account.AccountName, comparer)
                .ThenBy(account => account.Id)
                .ToArray(),
            AccountSortMode.AccountName => accounts
                .OrderBy(account => account.AccountName, comparer)
                .ThenBy(account => account.Issuer, comparer)
                .ThenBy(account => account.Id)
                .ToArray(),
            AccountSortMode.AccountNameDescending => accounts
                .OrderByDescending(account => account.AccountName, comparer)
                .ThenByDescending(account => account.Issuer, comparer)
                .ThenBy(account => account.Id)
                .ToArray(),
            _ => accounts
                .OrderBy(account => account.Issuer, comparer)
                .ThenBy(account => account.AccountName, comparer)
                .ThenBy(account => account.Id)
                .ToArray()
        };
    }

    public void ResumeRowCodeGeneration()
    {
        if (_autoGenerateCodeOnSelection && _allAccounts.Count > 0)
            StartRowCodeLifetime(refreshImmediately: true);
    }

    private void StartRowCodeLifetime(bool refreshImmediately)
    {
        var previousLifetime = _rowCodeLifetime;
        _rowCodeLifetime = null;
        previousLifetime?.Cancel();
        previousLifetime?.Dispose();

        var lifetime = new CancellationTokenSource();
        _rowCodeLifetime = lifetime;
        _ = RunRowCodeLifetimeAsync(lifetime, refreshImmediately);
    }

    private async Task RunRowCodeLifetimeAsync(
        CancellationTokenSource lifetime,
        bool refreshImmediately)
    {
        var cancellationToken = lifetime.Token;
        try
        {
            if (refreshImmediately)
                await RefreshAccountRowsAsync(GetTrackedAccounts(), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_countdownTickInterval, cancellationToken);
                var accounts = GetTrackedAccounts();
                var expiringAccounts = accounts
                    .Where(account => account.RemainingSeconds <= 1)
                    .ToArray();
                foreach (var account in accounts.Except(expiringAccounts))
                    account.Tick();

                if (_selectedAccount is not null)
                    ProjectSelectedCode(_selectedAccount);

                if (expiringAccounts.Length > 0)
                    await RefreshAccountRowsAsync(expiringAccounts, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_rowCodeLifetime, lifetime))
            {
                _rowCodeLifetime = null;
                lifetime.Dispose();
            }
        }
    }

    private async Task RefreshAccountRowsAsync(
        IReadOnlyList<AccountListItemViewModel> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0) return;

        FluentResults.Result<AccountTotpGenerationBatch> refreshed;
        try
        {
            refreshed = await _accountTotpService.GenerateManyAsync(
                accounts.Select(account => account.Id).ToArray());
        }
        catch (Exception)
        {
            foreach (var account in accounts)
                account.ClearCode();
            SetLocalizedCodeMessage(AvaloniaStringKeys.CodeRefreshFailed);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;
        if (refreshed.IsFailed)
        {
            foreach (var account in accounts)
                account.ClearCode();
            SetLocalizedCodeMessage(AvaloniaStringKeys.CodeRefreshFailed);
            return;
        }

        var refreshFailed = refreshed.Value.FailedAccountIds.Count > 0;
        foreach (var account in accounts)
        {
            if (!GetTrackedAccounts().Contains(account)) continue;
            if (!refreshed.Value.Codes.TryGetValue(account.Id, out var generated))
            {
                account.ClearCode();
                refreshFailed = true;
                continue;
            }

            account.UpdateCode(
                generated.Code,
                generated.RemainingSeconds,
                generated.PeriodSeconds);
            if (ReferenceEquals(account, _selectedAccount))
                ProjectSelectedCode(account);
        }

        if (refreshFailed)
            SetLocalizedCodeMessage(AvaloniaStringKeys.CodeRefreshFailed);
    }

    private IReadOnlyList<AccountListItemViewModel> GetTrackedAccounts()
    {
        if (_selectedAccount is null || _allAccounts.Contains(_selectedAccount))
            return _allAccounts;

        return [_selectedAccount];
    }

    private void ProjectSelectedCode(AccountListItemViewModel account)
    {
        GeneratedCode = account.Code;
        RemainingSeconds = account.RemainingSeconds;
        PeriodSeconds = account.PeriodSeconds;
    }

    private void ClearSelectedCodeProjection()
    {
        GeneratedCode = string.Empty;
        RemainingSeconds = 0;
        PeriodSeconds = 0;
        CodeMessage = string.Empty;
    }

    public void Dispose()
    {
        _brandIconResolver.CatalogChanged -= BrandCatalogChanged;
        _localization.CultureChanged -= LocalizationCultureChanged;
        Notification.PropertyChanged -= NotificationPropertyChanged;
        Notification.Dispose();
        ClearCopyConfirmation();
        ClearRecentHighlight();
        StopAndClearRowCodes();
        ClearQrImage();
        ClearEditor();
    }

    public void Clear()
    {
        StopAndClearRowCodes();
        ClearNotification();
        ClearCopyConfirmation();
        ClearRecentHighlight();
        ContextAccount = null;
        SelectedAccount = null;
        SearchText = string.Empty;
        _allAccounts = [];
        _selectedGroupId = null;
        _showFavoritesOnly = false;
        _showAllAccounts = false;
        RefreshFavoriteState();
        RefreshGroups();
        Accounts = [];
        ClearQrImage();
        ClearEditor();
        ClearGroupEditor();
    }

    public void ClearSensitiveOutput()
    {
        StopAndClearRowCodes();
        ClearQrImage();
        ClearEditor();
    }

    private void StopAndClearRowCodes()
    {
        var lifetime = _rowCodeLifetime;
        _rowCodeLifetime = null;
        lifetime?.Cancel();
        lifetime?.Dispose();
        foreach (var account in GetTrackedAccounts())
            account.ClearCode();
        ClearSelectedCodeProjection();
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(QrPreviewSize));
        OnPropertyChanged(nameof(FavoriteGroupColor));
        OnPropertyChanged(nameof(FavoriteGroupBackground));
        OnPropertyChanged(nameof(FavoriteGroupForeground));
        var visible = _brandIconResolver.ShowIssuerLogo;
        foreach (var account in _allAccounts)
            account.UpdateLogoVisibility(visible);
    }

    private void ClearEditor()
    {
        _editingAccountId = null;
        OnPropertyChanged(nameof(IsEditingExistingAccount));
        EditorIssuer = string.Empty;
        EditorAccountName = string.Empty;
        EditorSecret = string.Empty;
        EditorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
        EditorIsFavorite = false;
        IsAdvancedOptionsExpanded = false;
        EditorIssuerMessage = string.Empty;
        EditorSecretMessage = string.Empty;
        EditorPeriodMessage = string.Empty;
        EditorMessage = string.Empty;
        SelectBrandIconOption(null);
        IsEditorVisible = false;
    }

    private void ClearGroupEditor()
    {
        SetGroupEditorMode(null, isEditingFavorites: false);
        GroupEditorName = string.Empty;
        GroupEditorMessage = string.Empty;
        GroupEditorSearchText = string.Empty;
        _allGroupEditorAccounts = [];
        GroupEditorAccounts = [];
        RefreshGroupColorOptions(null);
        IsGroupEditorVisible = false;
    }

    private void SetGroupEditorMode(Guid? groupId, bool isEditingFavorites)
    {
        _editingGroupId = groupId;
        _isEditingFavorites = isEditingFavorites;
        OnPropertyChanged(nameof(IsEditingExistingGroup));
        OnPropertyChanged(nameof(IsEditingFavorites));
        OnPropertyChanged(nameof(IsCreatingGroup));
    }

    private void RefreshBrandIconOptions(string? selectedBrandId)
    {
        var automatic = new BrandIconOption(
            null,
            _localization.GetString(AvaloniaStringKeys.AutomaticBrandIcon));
        var available = _brandIconPackService?.AvailableBrands ?? [];
        EditorBrandIconOptions =
        [
            automatic,
            .. available.Select(brand => new BrandIconOption(brand.Id, brand.DisplayName))
        ];
        SelectBrandIconOption(selectedBrandId);
    }

    private void SelectBrandIconOption(string? brandId)
    {
        SelectedEditorBrandIconOption = EditorBrandIconOptions.FirstOrDefault(option =>
            string.Equals(option.Id, brandId, StringComparison.OrdinalIgnoreCase))
            ?? EditorBrandIconOptions.FirstOrDefault();
    }

    private void RefreshGroups()
    {
        _allGroups = _allAccounts
            .Where(account => account.Group is not null)
            .GroupBy(account => account.Group!.Id)
            .Select(group =>
            {
                var stored = group.First().Group!;
                var normalized = new AccountGroup(
                    stored.Id,
                    stored.Name,
                    NormalizeGroupColor(stored.Color));
                return new AccountGroupListItemViewModel(
                    normalized,
                    group.Count(),
                    normalized.Id == _selectedGroupId,
                    new AsyncCommand(() => SelectGroupAsync(normalized.Id), () => !IsBusy),
                    new AsyncCommand(() => BeginEditGroupAsync(normalized.Id), () => !IsBusy),
                    new AsyncCommand(
                        () => DeleteGroupAsync(normalized.Id, normalized.Name),
                        () => !IsBusy));
            })
            .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (_selectedGroupId.HasValue && _allGroups.All(group => group.Id != _selectedGroupId))
            _selectedGroupId = null;
        ApplyGroupSearch();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasAccountNavigationCards));
        OnPropertyChanged(nameof(ShouldShowAccountNavigationCards));
        OnPropertyChanged(nameof(AllAccountCount));
        OnPropertyChanged(nameof(HasAnyAccounts));
        OnPropertyChanged(nameof(IsAllAccountsFilterSelected));
        OnPropertyChanged(nameof(UngroupedCount));
        OnPropertyChanged(nameof(HasUngroupedAccounts));
        OnPropertyChanged(nameof(IsUngroupedFilterSelected));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        _clearGroupFilterCommand.NotifyCanExecuteChanged();
        _beginAddGroupCommand.NotifyCanExecuteChanged();
        _selectUngroupedCommand.NotifyCanExecuteChanged();
        _toggleAllAccountsFilterCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFavoriteState(bool favoriteAvailabilityChanged = true)
    {
        if (_showFavoritesOnly && !HasFavoriteAccounts)
            _showFavoritesOnly = false;
        OnPropertyChanged(nameof(FavoriteCount));
        if (favoriteAvailabilityChanged)
        {
            OnPropertyChanged(nameof(HasFavoriteAccounts));
            OnPropertyChanged(nameof(HasAccountNavigationCards));
            OnPropertyChanged(nameof(ShouldShowAccountNavigationCards));
        }
        OnPropertyChanged(nameof(HasMultipleAccounts));
        OnPropertyChanged(nameof(IsFavoritesFilterSelected));
        OnPropertyChanged(nameof(IsUngroupedFilterSelected));
        OnPropertyChanged(nameof(HasSelectedAccountNavigationCard));
        OnPropertyChanged(nameof(HasActiveAccountFilter));
        if (favoriteAvailabilityChanged)
        {
            _toggleFavoritesFilterCommand.NotifyCanExecuteChanged();
            _beginEditFavoritesCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshSortOptions(AccountSortMode selectedMode)
    {
        SortOptions =
        [
            new(
                AccountSortMode.Issuer,
                _localization.GetString(AvaloniaStringKeys.SortByIssuer),
                selectedMode == AccountSortMode.Issuer),
            new(
                AccountSortMode.IssuerDescending,
                _localization.GetString(AvaloniaStringKeys.SortByIssuerDescending),
                selectedMode == AccountSortMode.IssuerDescending),
            new(
                AccountSortMode.AccountName,
                _localization.GetString(AvaloniaStringKeys.SortByAccountName),
                selectedMode == AccountSortMode.AccountName),
            new(
                AccountSortMode.AccountNameDescending,
                _localization.GetString(AvaloniaStringKeys.SortByAccountNameDescending),
                selectedMode == AccountSortMode.AccountNameDescending)
        ];
        _selectedSortOption = null;
        SelectedSortOption = SortOptions.First(option => option.Mode == selectedMode);
    }

    private Task SelectSortAsync(AccountSortMode mode)
    {
        SelectedSortOption = SortOptions.First(option => option.Mode == mode);
        return Task.CompletedTask;
    }

    private void ApplyGroupSearch()
    {
        var terms = SearchText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Groups = terms.Length == 0
            ? _allGroups
            : _allGroups
                .Where(group => terms.All(term =>
                    group.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || _allAccounts.Any(account =>
                        account.Group?.Id == group.Id
                        && (account.Issuer.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || account.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase)))))
                .ToArray();
    }

    private void RefreshGroupColorOptions(string? selectedColor)
    {
        GroupColorOptions =
        [
            new("#4C956C", _localization.GetString(AvaloniaStringKeys.GroupColorGreen)),
            new("#18A999", _localization.GetString(AvaloniaStringKeys.GroupColorTurquoise)),
            new("#4F6BED", _localization.GetString(AvaloniaStringKeys.GroupColorBlue)),
            new("#F59E0B", _localization.GetString(AvaloniaStringKeys.GroupColorOrange)),
            new("#E45757", _localization.GetString(AvaloniaStringKeys.GroupColorRed)),
            new("#B455C7", _localization.GetString(AvaloniaStringKeys.GroupColorPurple))
        ];
        SelectedGroupColor = GroupColorOptions.FirstOrDefault(option =>
            string.Equals(option.Hex, selectedColor, StringComparison.OrdinalIgnoreCase))
            ?? GroupColorOptions[0];
    }

    private static string NormalizeGroupColor(string? color)
        => AccountGroupPolicy.NormalizeColor(color);

    private static string NormalizeFavoriteGroupColor(string? color)
    {
        var normalized = color?.Trim().ToUpperInvariant();
        return normalized is not null
            && AccountGroupPolicy.AllowedColors.Contains(normalized, StringComparer.Ordinal)
                ? normalized
                : AppSettings.DefaultFavoriteGroupColor;
    }

    private void NotifyCrudCommands()
    {
        _beginAddCommand.NotifyCanExecuteChanged();
        _toggleAccountFavoriteCommand.NotifyCanExecuteChanged();
        _beginEditCommand.NotifyCanExecuteChanged();
        _saveAccountCommand.NotifyCanExecuteChanged();
        _cancelEditCommand.NotifyCanExecuteChanged();
        _clearEditorPeriodCommand.NotifyCanExecuteChanged();
        _deleteAccountCommand.NotifyCanExecuteChanged();
        _beginContextEditCommand.NotifyCanExecuteChanged();
        _generateContextQrCommand.NotifyCanExecuteChanged();
        _deleteContextAccountCommand.NotifyCanExecuteChanged();
        _beginAddGroupCommand.NotifyCanExecuteChanged();
        _beginEditFavoritesCommand.NotifyCanExecuteChanged();
        _saveGroupCommand.NotifyCanExecuteChanged();
        _cancelGroupEditCommand.NotifyCanExecuteChanged();
    }

    private void SetLocalizedCodeMessage(string key, params object[] arguments)
    {
        ClearNotification();
        CodeMessage = string.Format(_localization.GetString(key), arguments);
        _codeMessageLocalizationKey = key;
        _codeMessageLocalizationArguments = arguments;
    }

    private void LocalizationCultureChanged(object? sender, EventArgs e)
    {
        RefreshSortOptions(SelectedSortOption?.Mode ?? AccountSortMode.Issuer);
        RefreshBrandIconOptions(SelectedEditorBrandIconOption?.Id);
        RefreshGroupColorOptions(SelectedGroupColor?.Hex);
        RefreshGroups();
        OnPropertyChanged(nameof(SearchResultSummary));
        OnPropertyChanged(nameof(AccountNavigationBackLabel));
        foreach (var account in _allAccounts)
            account.UpdateCustomPeriodLabel(FormatCustomPeriod(account.ConfiguredPeriodSeconds));

        if (_copyConfirmationAccount is { HasCopyConfirmation: true } copiedAccount)
            copiedAccount.ShowCopyConfirmation(
                _localization.GetString(AvaloniaStringKeys.CodeCopied));

        if (_codeMessageLocalizationKey is { } key)
        {
            var arguments = _codeMessageLocalizationArguments;
            SetLocalizedCodeMessage(key, arguments);
        }

        RelocalizeNotification();
    }

    private void BrandCatalogChanged(object? sender, EventArgs args)
    {
        RefreshBrandIconOptions(SelectedEditorBrandIconOption?.Id);
        foreach (var account in _allAccounts)
        {
            account.UpdateBrand(_brandIconResolver.ResolveAccount(
                account.Id,
                account.Issuer,
                account.AccountName));
            account.UpdateLogoVisibility(_brandIconResolver.ShowIssuerLogo);
        }
    }

    private void ShowLocalizedTransientNotification(
        string key,
        NotificationSeverity severity,
        params object[] arguments)
    {
        CodeMessage = string.Empty;
        _notificationLocalizationKey = key;
        _notificationLocalizationArguments = arguments;
        var message = string.Format(_localization.GetString(key), arguments);
        Notification.ShowTransient(message, severity);
    }

    private void RelocalizeNotification()
    {
        if (_notificationLocalizationKey is not { } key || !Notification.HasMessage)
            return;

        var message = string.Format(
            _localization.GetString(key),
            _notificationLocalizationArguments);
        Notification.ShowTransient(message, Notification.Severity);
    }

    private string FormatCustomPeriod(int periodSeconds) => string.Format(
        _localization.GetString(AvaloniaStringKeys.CustomPeriodFormat),
        periodSeconds);

    private void ClearQrImage()
    {
        _qrPreviewDialogs?.Close();
        var image = _qrImage;
        _qrImage = null;
        OnPropertyChanged(nameof(QrImage));
        OnPropertyChanged(nameof(HasQrImage));
        image?.Dispose();
    }

    private void ShowTransientMessage(string message)
    {
        ClearNotificationLocalization();
        Notification.ShowTransient(message, NotificationSeverity.Success);
    }

    private void ShowError(string message)
    {
        ClearNotificationLocalization();
        Notification.ShowTransient(message, NotificationSeverity.Error);
    }

    private void ClearNotification()
    {
        ClearNotificationLocalization();
        Notification.Clear();
    }

    private void ClearNotificationLocalization()
    {
        _notificationLocalizationKey = null;
        _notificationLocalizationArguments = [];
    }

    private void NotificationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(NotificationState.Text)
            or nameof(NotificationState.HasMessage))) return;
        if (!Notification.HasMessage)
            ClearNotificationLocalization();
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(ShouldShowAccountNavigationCards));
    }

    private void ShowCopyConfirmation(AccountListItemViewModel account)
    {
        ClearCopyConfirmation();
        account.ShowCopyConfirmation(_localization.GetString(AvaloniaStringKeys.CodeCopied));
        _copyConfirmationAccount = account;
        var lifetime = new CancellationTokenSource();
        _copyConfirmationLifetime = lifetime;
        _ = ClearCopyConfirmationAfterDelayAsync(account, lifetime);
    }

    private async Task ClearCopyConfirmationAfterDelayAsync(
        AccountListItemViewModel account,
        CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(_copyConfirmationDuration, lifetime.Token);
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

    private void StartRecentHighlightLifetime(AccountListItemViewModel? highlightedAccount)
    {
        _recentHighlightLifetime?.Cancel();
        _recentHighlightLifetime = null;
        if (highlightedAccount is null) return;

        var lifetime = new CancellationTokenSource();
        _recentHighlightLifetime = lifetime;
        _ = ClearRecentHighlightAfterAnimationAsync(highlightedAccount, lifetime);
    }

    private async Task ClearRecentHighlightAfterAnimationAsync(
        AccountListItemViewModel highlightedAccount,
        CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1100), lifetime.Token);
            if (ReferenceEquals(_recentHighlightLifetime, lifetime))
                highlightedAccount.ClearRecentlyAdded();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_recentHighlightLifetime, lifetime))
                _recentHighlightLifetime = null;
            lifetime.Dispose();
        }
    }

    private void ClearRecentHighlight()
    {
        var lifetime = _recentHighlightLifetime;
        _recentHighlightLifetime = null;
        lifetime?.Cancel();
        foreach (var account in _allAccounts)
            account.ClearRecentlyAdded();
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
