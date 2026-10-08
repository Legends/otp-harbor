namespace TOTP.Avalonia.Desktop;

public partial class SettingsWindow : global::Avalonia.Controls.Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void FaqSectionExpanded(
        object? sender,
        global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not global::Avalonia.Controls.Expander expanded) return;

        foreach (var section in new[] { FaqIconPacksExpander, FaqImportFormatsExpander })
        {
            if (!ReferenceEquals(section, expanded)) section.IsExpanded = false;
        }
    }

    private void FaqImportFormatSectionExpanded(
        object? sender,
        global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not global::Avalonia.Controls.Expander expanded) return;

        foreach (var section in new[]
                 {
                     FaqImportFormatsAegisExpander,
                     FaqImportFormatsTwoFasExpander,
                     FaqImportFormatsOtpAuthExpander
                 })
        {
            if (!ReferenceEquals(section, expanded)) section.IsExpanded = false;
        }

        e.Handled = true;
    }

    private void MoveWindow(
        object? sender,
        global::Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind
            != global::Avalonia.Input.PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        // Keep the custom, decoration-free title area reliably draggable while
        // leaving the close button and every other focusable control interactive.
        if (point.Position.Y > 80 || IsInteractiveSource(e.Source)) return;

        BeginMoveDrag(e);
    }

    private static bool IsInteractiveSource(object? source)
    {
        for (var visual = source as global::Avalonia.Visual;
             visual is not null;
             visual = global::Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual))
        {
            if (visual is global::Avalonia.Controls.Control { Focusable: true }) return true;
            if (visual is SettingsWindow) break;
        }

        return false;
    }
}
