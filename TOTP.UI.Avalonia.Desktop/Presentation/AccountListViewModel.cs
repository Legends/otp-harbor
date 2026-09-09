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
    private readonly TimeSpan _countdownTickInterval;
    private readonly ISettingsService? _settingsService;
    private readonly IAvaloniaQrPreviewDialogService? _qrPreviewDialogs;
    private readonly AsyncCommand _loadCommand;
    private readonly AsyncCommand _generateCommand;
    private readonly AsyncCommand _copyCommand;
    private readonly AsyncCommand<AccountListItemViewModel> _copyAccountCodeCommand;
    private readonly AsyncCommand _generateQrCommand;
    private readonly AsyncCommand _beginAddCommand;
    private readonly AsyncCommand _beginEditCommand;
    private readonly AsyncCommand _saveAccountCommand;
    private readonly AsyncCommand _cancelEditCommand;
    private readonly AsyncCommand _deleteAccountCommand;
    private readonly AsyncCommand _beginContextEditCommand;
    private readonly AsyncCommand _generateContextQrCommand;
    private readonly AsyncCommand _deleteContextAccountCommand;
    private CancellationTokenSource? _rowCodeLifetime;
    private CancellationTokenSource? _recentHighlightLifetime;
    private IReadOnlyList<AccountListItemViewModel> _allAccounts = [];
    private IReadOnlyList<AccountListItemViewModel> _accounts = [];
    private string _searchText = string.Empty;
    private AccountListItemViewModel? _selectedAccount;
    private AccountListItemViewModel? _contextAccount;
    private string _generatedCode = string.Empty;
    private string _codeMessage = string.Empty;
    private string? _codeMessageLocalizationKey;
    private object[] _codeMessageLocalizationArguments = [];
    private string? _notificationLocalizationKey;
    private object[] _notificationLocalizationArguments = [];
    private bool _localizedNotificationIsTransient;
    private bool _isBusy;
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
    private bool _isAdvancedOptionsExpanded;
    private string _editorIssuerMessage = string.Empty;
    private string _editorSecretMessage = string.Empty;
    private string _editorPeriodMessage = string.Empty;
    private string _editorMessage = string.Empty;
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
        TimeSpan? transientMessageDuration = null)
    {
        _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        _accountTotpService = accountTotpService ?? throw new ArgumentNullException(nameof(accountTotpService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _accountQrCodeService = accountQrCodeService ?? throw new ArgumentNullException(nameof(accountQrCodeService));
        _qrImageFactory = qrImageFactory ?? throw new ArgumentNullException(nameof(qrImageFactory));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _countdownTickInterval = countdownTickInterval ?? TimeSpan.FromSeconds(1);
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
        _generateQrCommand = new AsyncCommand(GenerateQrAsync, () => _selectedAccount is not null);
        _beginAddCommand = new AsyncCommand(BeginAddAsync, () => !IsBusy && !IsEditorVisible);
        _beginEditCommand = new AsyncCommand(
            BeginEditAsync,
            () => !IsBusy && !IsEditorVisible && SelectedAccount is not null);
        _saveAccountCommand = new AsyncCommand(SaveAccountAsync, () => !IsBusy && IsEditorVisible);
        _cancelEditCommand = new AsyncCommand(CancelEditAsync, () => !IsBusy && IsEditorVisible);
        _deleteAccountCommand = new AsyncCommand(
            DeleteAccountAsync,
            () => !IsBusy && !IsEditorVisible && SelectedAccount is not null);
        _beginContextEditCommand = new AsyncCommand(
            BeginContextEditAsync,
            () => !IsBusy && !IsEditorVisible && ContextAccount is not null);
        _generateContextQrCommand = new AsyncCommand(
            GenerateContextQrAsync,
            () => !IsBusy && !IsEditorVisible && ContextAccount is not null);
        _deleteContextAccountCommand = new AsyncCommand(
            DeleteContextAccountAsync,
            () => !IsBusy && !IsEditorVisible && ContextAccount is not null);
        _localization.CultureChanged += LocalizationCultureChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<AccountListItemViewModel> Accounts
    {
        get => _accounts;
        private set
        {
            if (!SetField(ref _accounts, value)) return;
            OnPropertyChanged(nameof(HasNoAccounts));
            OnPropertyChanged(nameof(HasNoSearchResults));
        }
    }

    public bool HasNoAccounts =>
        !IsBusy && !HasMessage && _allAccounts.Count == 0;

    public bool HasNoSearchResults =>
        !IsBusy && !HasMessage && _allAccounts.Count > 0 && Accounts.Count == 0;

    public NotificationState Notification { get; }
    public string Message => Notification.Text;
    public bool HasMessage => Notification.HasMessage;

    public AccountListItemViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!SetField(ref _selectedAccount, value)) return;
            ClearSelectedCodeProjection();
            ClearQrImage();
            _generateCommand.NotifyCanExecuteChanged();
            _generateQrCommand.NotifyCanExecuteChanged();
            _beginEditCommand.NotifyCanExecuteChanged();
            _deleteAccountCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasSelectedAccount));
            if (_autoGenerateCodeOnSelection && _selectedAccount is not null)
                _generateCommand.Execute(null);
        }
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
            ApplyFilter();
        }
    }

    public bool HasSearchText => SearchText.Length > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            _loadCommand.NotifyCanExecuteChanged();
            NotifyCrudCommands();
            OnPropertyChanged(nameof(HasNoAccounts));
            OnPropertyChanged(nameof(HasNoSearchResults));
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
    public ICommand DeleteAccountCommand => _deleteAccountCommand;
    public ICommand BeginContextEditCommand => _beginContextEditCommand;
    public ICommand GenerateContextQrCommand => _generateContextQrCommand;
    public ICommand DeleteContextAccountCommand => _deleteContextAccountCommand;

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

    public Task LoadAsync() => LoadAsync(null);

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
                Accounts = [];
                ShowError(_localization.GetString(AvaloniaStringKeys.AccountsLoadFailed));
                return;
            }

            SelectedAccount = null;
            _allAccounts = result.Value
                .Select(account => new AccountListItemViewModel(
                    account.ID,
                    account.Issuer,
                    account.AccountName ?? string.Empty,
                    account.ID == recentlyAddedAccountId,
                    _copyAccountCodeCommand,
                    account.PeriodSeconds,
                    FormatCustomPeriod(account.PeriodSeconds)))
                .ToArray();
            ApplyFilter();
            StartRecentHighlightLifetime(
                _allAccounts.FirstOrDefault(account => account.IsRecentlyAdded));
            if (_autoGenerateCodeOnSelection)
                StartRowCodeLifetime(refreshImmediately: true);
        }
        catch (Exception)
        {
            _allAccounts = [];
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
            EditorPeriodMessage = string.Empty;
            EditorMessage = string.Empty;
        }
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

        if (_settingsService?.Current.ClearClipboardEnabled == false)
        {
            var copyResult = await _clipboardService.CopyAsync(code);
            if (copyResult.IsSuccess)
            {
                ShowLocalizedTransientNotification(
                    AvaloniaStringKeys.CodeCopied,
                    NotificationSeverity.Information);
            }
            else
            {
                ShowLocalizedPersistentNotification(
                    AvaloniaStringKeys.ClipboardCopyUnavailable,
                    NotificationSeverity.Error);
            }

            return;
        }

        var configuredLifetime = _settingsService?.Current.ClearClipboardSeconds
            ?? remainingSeconds;
        var clearSeconds = Math.Max(1, Math.Min(remainingSeconds, configuredLifetime));
        var clearResult = await _clipboardService.CopyAndScheduleClearAsync(
            code,
            TimeSpan.FromSeconds(clearSeconds));
        if (clearResult.IsSuccess)
        {
            ShowLocalizedTransientNotification(
                AvaloniaStringKeys.CodeCopiedWithClear,
                NotificationSeverity.Information,
                clearSeconds);
            return;
        }

        var requiredCapabilities =
            ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear;
        if ((_clipboardService.Capabilities & requiredCapabilities) == ClipboardCapabilities.WriteText)
        {
            var fallbackResult = await _clipboardService.CopyAsync(code);
            if (fallbackResult.IsSuccess)
            {
                ShowLocalizedTransientNotification(
                    AvaloniaStringKeys.CodeCopiedWithoutClear,
                    NotificationSeverity.Warning);
            }
            else
            {
                ShowLocalizedPersistentNotification(
                    AvaloniaStringKeys.ClipboardCopyUnavailable,
                    NotificationSeverity.Error);
            }

            return;
        }

        ShowLocalizedPersistentNotification(
            AvaloniaStringKeys.ClipboardCopyUnavailable,
            NotificationSeverity.Error);
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
        if (IsBusy || IsEditorVisible) return Task.CompletedTask;
        ClearEditor();
        IsEditorVisible = true;
        return Task.CompletedTask;
    }

    public Task BeginEditAsync() => BeginEditAsync(SelectedAccount);

    public Task BeginContextEditAsync() => BeginEditAsync(ContextAccount);

    private async Task BeginEditAsync(AccountListItemViewModel? target)
    {
        if (IsBusy || IsEditorVisible || target is null) return;

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
            var normalizedSecret = SecretValidation.NormalizeBase32Secret(secret);
            var updated = new Account(
                _editingAccountId ?? Guid.NewGuid(),
                issuer,
                normalizedSecret,
                accountName.Length == 0 ? null : accountName,
                periodSeconds);
            var saved = _editingAccountId.HasValue
                ? await UpdateExistingAsync(loaded.Value, updated)
                : await _accountManager.AddNewAsync(updated);
            if (saved.IsFailed)
            {
                EditorMessage = _localization.GetString(AvaloniaStringKeys.AccountSaveFailed);
                return;
            }

            ClearEditor();
            IsBusy = false;
            await LoadAsync(isNewAccount ? updated.ID : null);
            if (!HasMessage)
            {
                SelectedAccount = Accounts.FirstOrDefault(account => account.Id == updated.ID);
                ShowTransientMessage(_localization.GetString(AvaloniaStringKeys.AccountSaved));
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

        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == accountId);
        ShowTransientMessage(successMessage);
    }

    public Task DeleteAccountAsync() => DeleteAccountAsync(SelectedAccount);

    public Task DeleteContextAccountAsync() => DeleteAccountAsync(ContextAccount);

    private async Task DeleteAccountAsync(AccountListItemViewModel? target)
    {
        if (IsBusy || IsEditorVisible || target is null) return;

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

    private void ApplyFilter()
    {
        Accounts = AccountListFilter.Apply(_allAccounts, SearchText);
        if (SelectedAccount is not null
            && !Accounts.Any(account => account.Id == SelectedAccount.Id))
        {
            SelectedAccount = null;
        }
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
                foreach (var account in accounts)
                    account.Tick();

                if (_selectedAccount is not null)
                    ProjectSelectedCode(_selectedAccount);

                var expiredAccounts = accounts
                    .Where(account => account.RemainingSeconds == 0)
                    .ToArray();
                if (expiredAccounts.Length > 0)
                    await RefreshAccountRowsAsync(expiredAccounts, cancellationToken);
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
        foreach (var account in accounts)
            account.ClearCode();
        if (accounts.Count == 0) return;

        FluentResults.Result<AccountTotpGenerationBatch> refreshed;
        try
        {
            refreshed = await _accountTotpService.GenerateManyAsync(
                accounts.Select(account => account.Id).ToArray());
        }
        catch (Exception)
        {
            SetLocalizedCodeMessage(AvaloniaStringKeys.CodeRefreshFailed);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;
        if (refreshed.IsFailed)
        {
            SetLocalizedCodeMessage(AvaloniaStringKeys.CodeRefreshFailed);
            return;
        }

        var refreshFailed = refreshed.Value.FailedAccountIds.Count > 0;
        foreach (var account in accounts)
        {
            if (!GetTrackedAccounts().Contains(account)) continue;
            if (!refreshed.Value.Codes.TryGetValue(account.Id, out var generated))
            {
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
        _localization.CultureChanged -= LocalizationCultureChanged;
        Notification.PropertyChanged -= NotificationPropertyChanged;
        Notification.Dispose();
        ClearRecentHighlight();
        StopAndClearRowCodes();
        ClearQrImage();
        ClearEditor();
    }

    public void Clear()
    {
        StopAndClearRowCodes();
        ClearNotification();
        ClearRecentHighlight();
        ContextAccount = null;
        SelectedAccount = null;
        SearchText = string.Empty;
        _allAccounts = [];
        Accounts = [];
        ClearQrImage();
        ClearEditor();
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

    public void NotifySettingsChanged() => OnPropertyChanged(nameof(QrPreviewSize));

    private void ClearEditor()
    {
        _editingAccountId = null;
        OnPropertyChanged(nameof(IsEditingExistingAccount));
        EditorIssuer = string.Empty;
        EditorAccountName = string.Empty;
        EditorSecret = string.Empty;
        EditorPeriodSeconds = TotpPeriodPolicy.DefaultSeconds;
        IsAdvancedOptionsExpanded = false;
        EditorIssuerMessage = string.Empty;
        EditorSecretMessage = string.Empty;
        EditorPeriodMessage = string.Empty;
        EditorMessage = string.Empty;
        IsEditorVisible = false;
    }

    private void NotifyCrudCommands()
    {
        _beginAddCommand.NotifyCanExecuteChanged();
        _beginEditCommand.NotifyCanExecuteChanged();
        _saveAccountCommand.NotifyCanExecuteChanged();
        _cancelEditCommand.NotifyCanExecuteChanged();
        _deleteAccountCommand.NotifyCanExecuteChanged();
        _beginContextEditCommand.NotifyCanExecuteChanged();
        _generateContextQrCommand.NotifyCanExecuteChanged();
        _deleteContextAccountCommand.NotifyCanExecuteChanged();
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
        foreach (var account in _allAccounts)
            account.UpdateCustomPeriodLabel(FormatCustomPeriod(account.ConfiguredPeriodSeconds));

        if (_codeMessageLocalizationKey is { } key)
        {
            var arguments = _codeMessageLocalizationArguments;
            SetLocalizedCodeMessage(key, arguments);
        }

        RelocalizeNotification();
    }

    private void ShowLocalizedTransientNotification(
        string key,
        NotificationSeverity severity,
        params object[] arguments) =>
        ShowLocalizedNotification(key, severity, isTransient: true, arguments);

    private void ShowLocalizedPersistentNotification(
        string key,
        NotificationSeverity severity,
        params object[] arguments) =>
        ShowLocalizedNotification(key, severity, isTransient: false, arguments);

    private void ShowLocalizedNotification(
        string key,
        NotificationSeverity severity,
        bool isTransient,
        params object[] arguments)
    {
        CodeMessage = string.Empty;
        _notificationLocalizationKey = key;
        _notificationLocalizationArguments = arguments;
        _localizedNotificationIsTransient = isTransient;
        var message = string.Format(_localization.GetString(key), arguments);
        if (isTransient)
            Notification.ShowTransient(message, severity);
        else
            Notification.ShowPersistent(message, severity);
    }

    private void RelocalizeNotification()
    {
        if (_notificationLocalizationKey is not { } key || !Notification.HasMessage)
            return;

        var message = string.Format(
            _localization.GetString(key),
            _notificationLocalizationArguments);
        if (_localizedNotificationIsTransient)
            Notification.ShowTransient(message, Notification.Severity);
        else
            Notification.ShowPersistent(message, Notification.Severity);
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
        Notification.ShowPersistent(message, NotificationSeverity.Error);
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
        _localizedNotificationIsTransient = false;
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
            await Task.Delay(TimeSpan.FromMilliseconds(1400), lifetime.Token);
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
