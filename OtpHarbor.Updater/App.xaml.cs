using System.Windows;
using System.Windows.Threading;

namespace TOTP.Updater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var arguments = UpdateInstallArguments.Parse(e.Args);
            var window = new UpdateInstallerWindow(arguments);
            MainWindow = window;
            window.Show();
            UpdaterBootstrapSupport.WriteBootstrapLog(arguments.LogPath, "updater window shown");
            UpdaterBootstrapSupport.WriteReadySignal(arguments.ReadySignalPath);

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(window.StartInstall));
        }
        catch (Exception ex)
        {
            var logPath = UpdaterBootstrapSupport.TryGetArgumentValue(e.Args, "--logPath");
            UpdaterBootstrapSupport.WriteBootstrapLog(
                logPath,
                $"updater bootstrap failed; exceptionType={ex.GetType().FullName}");
            MessageBox.Show(
                UpdaterText.StartupFailedMessage,
                UpdaterText.StartupFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
