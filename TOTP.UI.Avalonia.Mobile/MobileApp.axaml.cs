using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TOTP.Avalonia.Shared.Appearance;
using TOTP.Core.Services.Interfaces;
using TOTP.Avalonia.Mobile.Views;

namespace TOTP.Avalonia.Mobile;

public partial class MobileApp : Application
{
    private AvaloniaThemeService? _themeService;

    public Func<Control>? MainViewFactory { private get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = CreateMainView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            singleViewLifetime.MainView = CreateMainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ConfigureAppearance(IAppearanceSettingsService appearanceSettings)
    {
        ArgumentNullException.ThrowIfNull(appearanceSettings);
        _themeService?.Dispose();
        _themeService = new AvaloniaThemeService(
            PlatformSettings,
            appearanceSettings,
            ApplyTheme);
        _themeService.Start();
    }

    public void DisposeAppearance()
    {
        _themeService?.Dispose();
        _themeService = null;
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

    private Control CreateMainView() => MainViewFactory?.Invoke()
        ?? throw new InvalidOperationException("The mobile composition root was not configured.");
}
