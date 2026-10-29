using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TOTP.Installer.Engine;
using TOTP.Installer.Localization;
using TOTP.Installer.Presentation;
using TOTP.Installer.Views;

namespace TOTP.Installer;

internal sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var text = InstallerText.LoadCurrent();
            var window = new InstallerWindow();
            var viewModel = new InstallerViewModel(
                new BurnInstallerEngine(InstallerRuntime.Bootstrapper),
                text,
                () => window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
                LaunchFile,
                action =>
                {
                    if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
                    else global::Avalonia.Threading.Dispatcher.UIThread.Post(action);
                },
                result =>
                {
                    window.AllowClose();
                    InstallerRuntime.Bootstrapper.SetResult(result);
                    desktop.Shutdown(result);
                });
            window.Attach(viewModel);
            desktop.MainWindow = window;
            window.Show();
            viewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void LaunchFile(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
}
