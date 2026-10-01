using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TOTP.Avalonia.Desktop.Dialogs;

public partial class ConfirmationDialogWindow : Window
{
    private const double DestructiveDialogHorizontalMargin = 16;
    private Window? _positionOwner;

    public ConfirmationDialogWindow()
    {
        InitializeComponent();
    }

    internal void ConfigureForOwner(Window owner, bool isDestructive)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _positionOwner = owner;
        if (!isDestructive) return;

        var availableWidth = Math.Max(
            0,
            owner.ClientSize.Width - (DestructiveDialogHorizontalMargin * 2));
        if (availableWidth <= 0) return;

        MinWidth = Math.Min(300, availableWidth);
        Width = Math.Min(320, availableWidth);
        MaxWidth = availableWidth;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CenterWithinOwner();
        Dispatcher.UIThread.Post(CenterWithinOwner, DispatcherPriority.Loaded);
    }

    private void CenterWithinOwner()
    {
        var owner = _positionOwner ?? Owner;
        if (owner is null || owner.ClientSize.Width <= 0 || ClientSize.Width <= 0) return;

        var ownerOrigin = owner.PointToScreen(default);
        var ownerScale = owner.RenderScaling;
        var dialogScale = RenderScaling;
        var ownerWidth = owner.ClientSize.Width * ownerScale;
        var ownerHeight = owner.ClientSize.Height * ownerScale;
        var dialogWidth = ClientSize.Width * dialogScale;
        var dialogHeight = ClientSize.Height * dialogScale;
        Position = new PixelPoint(
            ownerOrigin.X + (int)Math.Round((ownerWidth - dialogWidth) / 2),
            ownerOrigin.Y + (int)Math.Round((ownerHeight - dialogHeight) / 2));
    }
}
