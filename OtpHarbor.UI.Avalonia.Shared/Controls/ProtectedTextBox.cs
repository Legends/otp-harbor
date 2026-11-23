using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace TOTP.Avalonia.Shared.Controls;

/// <summary>Retains text editing while concealing masked values from automation.</summary>
public sealed class ProtectedTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new ProtectedTextBoxAutomationPeer(this);

    private sealed class ProtectedTextBoxAutomationPeer(ProtectedTextBox owner)
        : TextBoxAutomationPeer(owner), IValueProvider
    {
        string? IValueProvider.Value => Owner.PasswordChar == '\0' ? Owner.Text : string.Empty;

        protected override void OwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBox.PasswordCharProperty
                || e.Property == TextBox.TextProperty && Owner.PasswordChar != '\0')
            {
                RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, string.Empty, string.Empty);
                return;
            }
            base.OwnerPropertyChanged(sender, e);
        }
    }
}
