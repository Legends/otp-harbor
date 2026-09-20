using Avalonia;
using Avalonia.Controls.Primitives;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class AccountRow : TemplatedControl
{
    private string _accessibleName = "Account";
    private string _primaryText = string.Empty;
    private string _secondaryText = string.Empty;
    private bool _hasSecondaryText;

    public static readonly StyledProperty<string> IssuerProperty =
        AvaloniaProperty.Register<AccountRow, string>(nameof(Issuer), string.Empty);

    public static readonly StyledProperty<string> AccountNameProperty =
        AvaloniaProperty.Register<AccountRow, string>(nameof(AccountName), string.Empty);

    public static readonly DirectProperty<AccountRow, string> AccessibleNameProperty =
        AvaloniaProperty.RegisterDirect<AccountRow, string>(
            nameof(AccessibleName),
            static control => control.AccessibleName);

    public static readonly DirectProperty<AccountRow, string> PrimaryTextProperty =
        AvaloniaProperty.RegisterDirect<AccountRow, string>(
            nameof(PrimaryText),
            static control => control.PrimaryText);

    public static readonly DirectProperty<AccountRow, string> SecondaryTextProperty =
        AvaloniaProperty.RegisterDirect<AccountRow, string>(
            nameof(SecondaryText),
            static control => control.SecondaryText);

    public static readonly DirectProperty<AccountRow, bool> HasSecondaryTextProperty =
        AvaloniaProperty.RegisterDirect<AccountRow, bool>(
            nameof(HasSecondaryText),
            static control => control.HasSecondaryText);

    static AccountRow()
    {
        IssuerProperty.Changed.AddClassHandler<AccountRow>(
            static (control, _) => control.UpdatePresentation());
        AccountNameProperty.Changed.AddClassHandler<AccountRow>(
            static (control, _) => control.UpdatePresentation());
    }

    public string Issuer
    {
        get => GetValue(IssuerProperty);
        set => SetValue(IssuerProperty, value ?? string.Empty);
    }

    public string AccountName
    {
        get => GetValue(AccountNameProperty);
        set => SetValue(AccountNameProperty, value ?? string.Empty);
    }

    public string AccessibleName
    {
        get => _accessibleName;
        private set => SetAndRaise(AccessibleNameProperty, ref _accessibleName, value);
    }

    public string PrimaryText
    {
        get => _primaryText;
        private set => SetAndRaise(PrimaryTextProperty, ref _primaryText, value);
    }

    public string SecondaryText
    {
        get => _secondaryText;
        private set => SetAndRaise(SecondaryTextProperty, ref _secondaryText, value);
    }

    public bool HasSecondaryText
    {
        get => _hasSecondaryText;
        private set => SetAndRaise(HasSecondaryTextProperty, ref _hasSecondaryText, value);
    }

    private void UpdatePresentation()
    {
        var issuer = Issuer.Trim();
        var accountName = AccountName.Trim();
        PrimaryText = issuer.Length > 0 ? issuer : accountName;
        SecondaryText = issuer.Length > 0 ? accountName : string.Empty;
        HasSecondaryText = SecondaryText.Length > 0;
        AccessibleName = (issuer.Length, accountName.Length) switch
        {
            ( > 0, > 0) => $"{issuer}, {accountName}",
            ( > 0, 0) => issuer,
            (0, > 0) => accountName,
            _ => "Account"
        };
    }
}
