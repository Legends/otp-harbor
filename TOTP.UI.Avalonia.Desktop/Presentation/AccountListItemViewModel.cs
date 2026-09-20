using System.ComponentModel;
using System.Windows.Input;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Validation;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class AccountListItemViewModel(
    Guid id,
    string issuer,
    string accountName,
    bool isRecentlyAdded = false,
    ICommand? copyCodeCommand = null,
    int configuredPeriodSeconds = TotpPeriodPolicy.DefaultSeconds,
    string customPeriodLabel = "",
    BrandInfo? brand = null) : INotifyPropertyChanged
{
    private bool _isRecentlyAdded = isRecentlyAdded;
    private string _code = string.Empty;
    private int _remainingSeconds;
    private int _periodSeconds;
    private string _customPeriodLabel = customPeriodLabel;
    private BrandInfo _brand = brand ?? BrandInfo.Generic(issuer);
    private bool _showIssuerLogo = true;

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

    public string Code
    {
        get => _code;
        private set
        {
            if (_code == value) return;
            _code = value;
            OnPropertyChanged(nameof(Code));
            OnPropertyChanged(nameof(DisplayCode));
            OnPropertyChanged(nameof(HasCode));
            OnPropertyChanged(nameof(IsExpiring));
        }
    }

    public string DisplayCode => Code.Length < 2
        ? Code
        : Code.Insert(Code.Length / 2, " ");

    public bool HasCode => Code.Length > 0;
    public bool IsExpiring => HasCode && RemainingSeconds is > 0 and <= 10;

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set
        {
            var normalized = Math.Max(0, value);
            if (_remainingSeconds == normalized) return;
            _remainingSeconds = normalized;
            OnPropertyChanged(nameof(RemainingSeconds));
            OnPropertyChanged(nameof(IsExpiring));
        }
    }

    public int PeriodSeconds
    {
        get => _periodSeconds;
        private set
        {
            var normalized = Math.Max(0, value);
            if (_periodSeconds == normalized) return;
            _periodSeconds = normalized;
            OnPropertyChanged(nameof(PeriodSeconds));
        }
    }

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

    public void UpdateCode(string code, int remainingSeconds, int periodSeconds)
    {
        Code = code;
        RemainingSeconds = Math.Max(1, remainingSeconds);
        PeriodSeconds = Math.Max(RemainingSeconds, periodSeconds);
    }

    public void Tick()
    {
        if (RemainingSeconds > 0)
            RemainingSeconds--;
    }

    public void ClearCode()
    {
        Code = string.Empty;
        RemainingSeconds = 0;
        PeriodSeconds = 0;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
