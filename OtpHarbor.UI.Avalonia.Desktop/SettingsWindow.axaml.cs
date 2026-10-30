namespace TOTP.Avalonia.Desktop;

public partial class SettingsWindow : global::Avalonia.Controls.Window
{
    private const double LinuxPreferredWidth = 720;
    private const double LinuxPreferredHeight = 760;
    private const double WorkingAreaMargin = 24;

    public SettingsWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsLinux())
        {
            Width = LinuxPreferredWidth;
            Height = LinuxPreferredHeight;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!OperatingSystem.IsLinux()) return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var availableWidth = Math.Max(
            1,
            (screen.WorkingArea.Width / scaling) - WorkingAreaMargin);
        var availableHeight = Math.Max(
            1,
            (screen.WorkingArea.Height / scaling) - WorkingAreaMargin);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(LinuxPreferredWidth, availableWidth);
        Height = Math.Min(LinuxPreferredHeight, availableHeight);
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
