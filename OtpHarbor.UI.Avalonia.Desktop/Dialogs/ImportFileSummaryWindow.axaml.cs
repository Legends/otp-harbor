using Avalonia.Controls;

namespace TOTP.Avalonia.Desktop.Dialogs;

public partial class ImportFileSummaryWindow : Window
{
    public ImportFileSummaryWindow() => InitializeComponent();

    internal void ConfigureForOwner(Window owner)
    {
        DialogWindowSizing.FitToOwner(this, owner);
        MaxHeight = Math.Max(240, owner.ClientSize.Height - 32);
        SummaryPanel.FindControl<ScrollViewer>("SummaryContentScroll")!.MaxHeight = Math.Min(360, Math.Max(80, MaxHeight - 100));
    }
}
