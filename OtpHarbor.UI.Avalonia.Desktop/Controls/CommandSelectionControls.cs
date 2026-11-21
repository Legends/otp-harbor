using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Automation.Peers;

namespace TOTP.Avalonia.Desktop.Controls;

// Commands own the bound selection, including cancellation and authorization failures.
public sealed class CommandToggleSwitch : ToggleSwitch
{
    protected override Type StyleKeyOverride => typeof(ToggleSwitch);
    protected override void Toggle() { }
}

public sealed class CommandRadioButton : RadioButton
{
    protected override Type StyleKeyOverride => typeof(RadioButton);
    protected override void Toggle() { }
    protected override AutomationPeer OnCreateAutomationPeer() => new CommandRadioButtonPeer(this);

    private sealed class CommandRadioButtonPeer(CommandRadioButton owner) : RadioButtonAutomationPeer(owner), ISelectionItemProvider
    {
        void ISelectionItemProvider.Select()
        {
            EnsureEnabled();
            ((CommandRadioButton)Owner).OnClick();
        }
    }
}
