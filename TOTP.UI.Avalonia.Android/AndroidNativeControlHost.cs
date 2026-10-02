using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace TOTP.Avalonia.Android;

internal abstract class AndroidNativeControlHost : NativeControlHost
{
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new NoneAutomationPeer(this);
}
