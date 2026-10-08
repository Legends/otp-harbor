using System.ComponentModel;
using System.Windows.Input;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Validation;
using TOTP.Core.Models;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class AccountListItemViewModel(
    Guid id,
    string issuer,
    string accountName,
    bool isRecentlyAdded = false,
    ICommand? copyCodeCommand = null,
    int configuredPeriodSeconds = TotpPeriodPolicy.DefaultSeconds,
    string customPeriodLabel = "",
    BrandInfo? brand = null,
    AccountGroup? group = null,
    bool isFavorite = false,
    ICommand? toggleFavoriteCommand = null) : INotifyPropertyChanged
{
    private bool _isRecentlyAdded = isRecentlyAdded;
    private string _code = string.Empty;
    private int _remainingSeconds;
    private int _periodSeconds;
    private string _customPeriodLabel = customPeriodLabel;
    private BrandInfo _brand = brand ?? BrandInfo.Generic(issuer);
    private bool _showIssuerLogo = true;
    private bool _isFavorite = isFavorite;
    private string _copyConfirmation = string.Empty;
    private bool _codeBindingsDirty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; } = id;
    public string Issuer { get; } = issuer;
    public string AccountName { get; } = accountName;
    public ICommand? CopyCodeCommand { get; } = copyCodeCommand;
    public int ConfiguredPeriodSeconds { get; } = configuredPeriodSeconds;
    public bool HasCustomPeriod => ConfiguredPeriodSeconds != TotpPeriodPolicy.DefaultSeconds;
    public string CustomPeriodLabel => _customPeriodLabel;
    public BrandInfo Brand => _brand;
    public bool ShowIssuerLogo => _showIssuerLogo;
    public AccountGroup? Group { get; } = group;
    public bool IsFavorite => _isFavorite;
    public ICommand? ToggleFavoriteCommand { get; } = toggleFavoriteCommand;

    public string CopyConfirmation
    {
        get => _copyConfirmation;
        private set
        {
            if (_copyConfirmation == value) return;
            _copyConfirmation = value;
            OnPropertyChanged(nameof(CopyConfirmation));
            OnPropertyChanged(nameof(HasCopyConfirmation));
        }
    }

    public bool HasCopyConfirmation => CopyConfirmation.Length > 0;

    public string Code => _code;

    public string DisplayCode => Code.Length < 2
        ? Code
        : Code.Insert(Code.Length / 2, " ");

    public bool HasCode => Code.Length > 0;
    public bool IsExpiring => HasCode && RemainingSeconds is > 0 and <= 10;

    public int RemainingSeconds => _remainingSeconds;

    public int PeriodSeconds => _periodSeconds;

    public bool IsRecentlyAdded
    {
        get => _isRecentlyAdded;
        private set
        {
            if (_isRecentlyAdded == value) return;
            _isRecentlyAdded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRecentlyAdded)));
        }
    }

    public void ClearRecentlyAdded() => IsRecentlyAdded = false;

    public void ShowCopyConfirmation(string message) =>
        CopyConfirmation = message ?? string.Empty;

    public void ClearCopyConfirmation() => CopyConfirmation = string.Empty;

    public void UpdateFavorite(bool isFavorite)
    {
        if (_isFavorite == isFavorite) return;
        _isFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
    }

    public void UpdateBrand(BrandInfo brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        if (ReferenceEquals(_brand, brand)) return;
        _brand = brand;
        OnPropertyChanged(nameof(Brand));
    }

    public void UpdateLogoVisibility(bool visible)
    {
        if (_showIssuerLogo == visible) return;
        _showIssuerLogo = visible;
        OnPropertyChanged(nameof(ShowIssuerLogo));
    }

    public void UpdateCustomPeriodLabel(string label)
    {
        if (_customPeriodLabel == label) return;
        _customPeriodLabel = label;
        OnPropertyChanged(nameof(CustomPeriodLabel));
    }

    public void UpdateCode(
        string code,
        int remainingSeconds,
        int periodSeconds,
        bool notifyBindings = true)
    {
        _code = code;
        _remainingSeconds = Math.Max(1, remainingSeconds);
        _periodSeconds = Math.Max(_remainingSeconds, periodSeconds);
        if (notifyBindings)
        {
            _codeBindingsDirty = false;
            NotifyCodeChanged();
        }
        else
        {
            _codeBindingsDirty = true;
        }
    }

    public void Tick(bool notifyBindings = true)
    {
        if (_remainingSeconds <= 0) return;

        var wasExpiring = IsExpiring;
        _remainingSeconds--;
        if (!notifyBindings)
        {
            _codeBindingsDirty = true;
            return;
        }

        if (_codeBindingsDirty)
        {
            _codeBindingsDirty = false;
            NotifyCodeChanged();
            return;
        }

        OnPropertyChanged(nameof(RemainingSeconds));
        if (wasExpiring != IsExpiring)
            OnPropertyChanged(nameof(IsExpiring));
    }

    public void ClearCode(bool notifyBindings = true)
    {
        if (_code.Length == 0 && _remainingSeconds == 0 && _periodSeconds == 0)
        {
            if (notifyBindings && _codeBindingsDirty)
            {
                _codeBindingsDirty = false;
                NotifyCodeChanged();
            }
            return;
        }

        _code = string.Empty;
        _remainingSeconds = 0;
        _periodSeconds = 0;
        if (notifyBindings)
        {
            _codeBindingsDirty = false;
            NotifyCodeChanged();
        }
        else
        {
            _codeBindingsDirty = true;
        }
    }

    public void RefreshCodeBindings()
    {
        if (!_codeBindingsDirty) return;
        _codeBindingsDirty = false;
        NotifyCodeChanged();
    }

    private void NotifyCodeChanged()
    {
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(HasCode));
        OnPropertyChanged(nameof(RemainingSeconds));
        OnPropertyChanged(nameof(PeriodSeconds));
        OnPropertyChanged(nameof(IsExpiring));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
