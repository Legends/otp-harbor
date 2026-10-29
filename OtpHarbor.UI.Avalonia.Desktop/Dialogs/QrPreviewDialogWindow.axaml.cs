using Avalonia.Controls;
using Avalonia.Input;

namespace TOTP.Avalonia.Desktop.Dialogs;

public partial class QrPreviewDialogWindow : Window
{
    public QrPreviewDialogWindow() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
