using Avalonia.Controls;

namespace TOTP.Avalonia.Desktop.Dialogs;

public partial class ImportReviewWindow : Window
{
    public ImportReviewWindow() => InitializeComponent();

    internal void FitToScreen(Window owner)
    {
        if (owner.Screens.ScreenFromWindow(owner) is not { } screen) return;
        var availableWidth = screen.WorkingArea.Width / owner.RenderScaling - 32;
        var availableHeight = screen.WorkingArea.Height / owner.RenderScaling - 64;
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }
}
