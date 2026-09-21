using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOTP.Avalonia.Shared.Branding;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class GroupAccountSelectionViewModel(
    Guid accountId,
    string issuer,
    string accountName,
    BrandInfo brand,
    bool showIssuerLogo,
    bool isSelected) : INotifyPropertyChanged
{
    private bool _isSelected = isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid AccountId { get; } = accountId;
    public string Issuer { get; } = issuer;
    public string AccountName { get; } = accountName;
    public BrandInfo Brand { get; } = brand;
    public bool ShowIssuerLogo { get; } = showIssuerLogo;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
