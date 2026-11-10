using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TOTP.Avalonia.Desktop.Dialogs;

public partial class CameraScannerDialogWindow : Window
{
    public CameraScannerDialogWindow()
    {
        InitializeComponent();
        Opened += (_, _) => OpenQrImageButton.Focus();
    }

    private void MoveWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void ShowKeyboardHelp(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional)
            ToolTip.SetIsOpen(ScannerHelpButton, true);
    }

    private void HideKeyboardHelp(object? sender, RoutedEventArgs e) =>
        ToolTip.SetIsOpen(ScannerHelpButton, false);
}
