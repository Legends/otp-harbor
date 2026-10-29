using System.Windows;

namespace TOTP.Updater;

public partial class UpdateInstallerWindow : Window
{
    private readonly UpdateInstallerViewModel _viewModel;
    private bool _installStarted;

    internal UpdateInstallerWindow(UpdateInstallArguments arguments)
    {
        InitializeComponent();
        _viewModel = new UpdateInstallerViewModel(
            new UpdateInstallerService(arguments));
        _viewModel.RequestClose += (_, _) => Close();
        DataContext = _viewModel;
    }

    public void StartInstall()
    {
        if (_installStarted)
        {
            return;
        }

        _installStarted = true;
        _ = _viewModel.RunInstallAsync();
    }
}
