using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOTP.Core.Validation;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Models;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed class MobileAccountItem(
    Guid id,
    string issuer,
    string accountName,
    int configuredPeriodSeconds = TotpPeriodPolicy.DefaultSeconds,
    string customPeriodLabel = "",
    BrandInfo? brand = null,
    bool isFavorite = false,
    string addToFavoritesText = "",
    string removeFromFavoritesText = "",
    AccountGroup? group = null) : INotifyPropertyChanged
{
    private string _code = string.Empty;
    private int _remainingSeconds;
    private int _periodSeconds = configuredPeriodSeconds;
    private string _customPeriodLabel = customPeriodLabel;
    private BrandInfo _brand = brand ?? BrandInfo.Generic(issuer);
    private bool _showIssuerLogo = true;
    private bool _isFavorite = isFavorite;
    private string _addToFavoritesText = addToFavoritesText;
    private string _removeFromFavoritesText = removeFromFavoritesText;
    private string _copyConfirmation = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; } = id;
    public string Issuer { get; } = issuer;
    public string AccountName { get; } = accountName;
    public int ConfiguredPeriodSeconds { get; } = configuredPeriodSeconds;
    public bool HasCustomPeriod => ConfiguredPeriodSeconds != TotpPeriodPolicy.DefaultSeconds;
    public string CustomPeriodLabel => _customPeriodLabel;
    public BrandInfo Brand => _brand;
    public bool ShowIssuerLogo => _showIssuerLogo;
    public AccountGroup? Group { get; } = group;
    public bool IsFavorite => _isFavorite;
    public string FavoriteActionText => IsFavorite
        ? _removeFromFavoritesText
        : _addToFavoritesText;
    public string CopyConfirmation => _copyConfirmation;
    public bool HasCopyConfirmation => CopyConfirmation.Length > 0;
    public bool HasAccountName => AccountName.Length > 0;
    public string Code => _code;
    public string DisplayCode => FormatCode(_code);
    public int RemainingSeconds => _remainingSeconds;
    public int PeriodSeconds => _periodSeconds;
    public bool IsExpiring => Code.Length > 0 && RemainingSeconds is > 0 and <= 10;

    public string DisplayName => HasAccountName
        ? $"{Issuer} · {AccountName}"
        : Issuer;

    internal void UpdateCode(string code, int remainingSeconds, int periodSeconds)
    {
        _code = code;
        _remainingSeconds = Math.Max(1, remainingSeconds);
        _periodSeconds = Math.Max(_remainingSeconds, periodSeconds);
        NotifyCodeChanged();
    }

    internal void Tick(bool notifyBindings = true)
    {
        if (_remainingSeconds <= 0) return;
        var wasExpiring = IsExpiring;
        _remainingSeconds--;
        if (!notifyBindings) return;
        OnPropertyChanged(nameof(RemainingSeconds));
        if (wasExpiring != IsExpiring)
            OnPropertyChanged(nameof(IsExpiring));
    }

    internal void RefreshCountdownBindings()
    {
        OnPropertyChanged(nameof(RemainingSeconds));
        OnPropertyChanged(nameof(IsExpiring));
    }

    internal void UpdateCustomPeriodLabel(string label)
    {
        if (_customPeriodLabel == label) return;
        _customPeriodLabel = label;
        OnPropertyChanged(nameof(CustomPeriodLabel));
    }

    internal void UpdateBrand(BrandInfo brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        if (ReferenceEquals(_brand, brand)) return;
        _brand = brand;
        OnPropertyChanged(nameof(Brand));
    }

    internal void UpdateLogoVisibility(bool visible)
    {
        if (_showIssuerLogo == visible) return;
        _showIssuerLogo = visible;
        OnPropertyChanged(nameof(ShowIssuerLogo));
    }

    internal void UpdateFavorite(bool isFavorite)
    {
        if (_isFavorite == isFavorite) return;
        _isFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteActionText));
    }

    internal void UpdateFavoriteLocalization(string addText, string removeText)
    {
        _addToFavoritesText = addText;
        _removeFromFavoritesText = removeText;
        OnPropertyChanged(nameof(FavoriteActionText));
    }

    internal void ShowCopyConfirmation(string message)
    {
        if (_copyConfirmation == message) return;
        _copyConfirmation = message;
        OnPropertyChanged(nameof(CopyConfirmation));
        OnPropertyChanged(nameof(HasCopyConfirmation));
    }

    internal void ClearCopyConfirmation() => ShowCopyConfirmation(string.Empty);

    internal void ClearCode()
    {
        _code = string.Empty;
        _remainingSeconds = 0;
        _periodSeconds = ConfiguredPeriodSeconds;
        NotifyCodeChanged();
    }

    private static string FormatCode(string code)
    {
        if (code.Length < 2) return code;
        var midpoint = code.Length / 2;
        return $"{code[..midpoint]} {code[midpoint..]}";
    }

    private void NotifyCodeChanged()
    {
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(RemainingSeconds));
        OnPropertyChanged(nameof(PeriodSeconds));
        OnPropertyChanged(nameof(IsExpiring));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
