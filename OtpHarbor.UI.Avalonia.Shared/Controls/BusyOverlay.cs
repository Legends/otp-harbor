using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class BusyOverlay : ContentControl
{
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(IsBusy));

    public static readonly StyledProperty<string> BusyMessageProperty =
        AvaloniaProperty.Register<BusyOverlay, string>(
            nameof(BusyMessage),
            string.Empty);

    static BusyOverlay()
    {
        IsBusyProperty.Changed.AddClassHandler<BusyOverlay>(
            static (control, _) =>
                control.PseudoClasses.Set(":busy", control.IsBusy));
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string BusyMessage
    {
        get => GetValue(BusyMessageProperty);
        set => SetValue(BusyMessageProperty, value ?? string.Empty);
    }
}
