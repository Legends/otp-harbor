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

    private void MoveWindow(
        object? sender,
        global::Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind
            != global::Avalonia.Input.PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        BeginMoveDrag(e);
    }
}
