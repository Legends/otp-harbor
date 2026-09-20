using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TOTP.Avalonia.Shared.Appearance;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Avalonia.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    private AvaloniaExceptionHooks? _exceptionHooks;
    private AvaloniaThemeService? _themeService;
    private AvaloniaBackgroundServiceCoordinator? _backgroundServices;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = AvaloniaCompositionRoot.Build(desktop);
            _themeService = new AvaloniaThemeService(
                PlatformSettings,
                _services.GetRequiredService<IAppearanceSettingsService>(),
                ApplyTheme);
            _themeService.Start();
            _exceptionHooks = new AvaloniaExceptionHooks(
                global::Avalonia.Threading.Dispatcher.UIThread,
                _services.GetRequiredService<AvaloniaExceptionBoundary>());
            _backgroundServices = _services.GetRequiredService<AvaloniaBackgroundServiceCoordinator>();
            _backgroundServices.Start();
            desktop.Exit += (_, _) =>
            {
                try
                {
                    _backgroundServices.Stop();
                }
                finally
                {
                    _exceptionHooks.Dispose();
                    _themeService.Dispose();
                    _services.Dispose();
                }
            };
            var mainWindow = _services.GetRequiredService<MainWindow>();
            var windows = _services.GetRequiredService<AvaloniaWindowCoordinator>();
            desktop.MainWindow = mainWindow;
            _services.GetRequiredService<IActivationListener>().Start(request =>
            {
                if (request.Kind != ApplicationActivationKind.ActivateMainWindow) return;
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    windows.ActivateCurrent();
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(global::Avalonia.Styling.ThemeVariant variant)
    {
        if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RequestedThemeVariant = variant;
            return;
        }

        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => RequestedThemeVariant = variant);
    }
}
