using Avalonia.Controls;
using Avalonia.Threading;

namespace TOTP.Avalonia.Desktop.Dialogs;

public partial class PasswordDialogWindow : Window
{
    public PasswordDialogWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Dispatcher.UIThread.Post(PasswordInput.FocusInput, DispatcherPriority.Input);
    }
}
