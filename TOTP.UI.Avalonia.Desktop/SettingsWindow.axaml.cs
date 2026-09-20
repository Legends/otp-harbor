namespace TOTP.Avalonia.Desktop;

public partial class SettingsWindow : global::Avalonia.Controls.Window
{
    public SettingsWindow()
    {
        InitializeComponent();
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
