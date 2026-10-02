using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TOTP.Avalonia.Shared.Appearance;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;
using Serilog;

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

            void DispatchActivation(ApplicationActivationRequest request)
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    windows.ActivateCurrent();
#if DEBUG
                    if (request.Kind == ApplicationActivationKind.DebugBulkAddSyntheticAccounts)
                    {
                        _ = AddDebugSyntheticAccountsAsync(
                            _services.GetRequiredService<AccountListViewModel>());
                        return;
                    }
                    if (request.Kind == ApplicationActivationKind.DebugBulkDeleteSyntheticAccounts)
                    {
                        _ = DeleteDebugSyntheticAccountsAsync(
                            _services.GetRequiredService<AccountListViewModel>());
                        return;
                    }
                    if (request.Kind == ApplicationActivationKind.DebugImportAccounts)
                    {
                        _ = ImportDebugAccountsAsync(
                            _services.GetRequiredService<AccountListViewModel>(),
                            request.Payload);
                        return;
                    }
                    if (request.Kind == ApplicationActivationKind.DebugDeleteAllAccounts)
                    {
                        _ = DeleteAllAccountsAsync(
                            _services.GetRequiredService<AccountListViewModel>());
                    }
#endif
                });
            }

            _services.GetRequiredService<IActivationListener>().Start(DispatchActivation);
#if DEBUG
            var initialActivation = Program.ResolveActivationRequest(desktop.Args ?? []);
            if (initialActivation.Kind != ApplicationActivationKind.ActivateMainWindow)
                DispatchActivation(initialActivation);
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

#if DEBUG
    private static async Task AddDebugSyntheticAccountsAsync(AccountListViewModel accountList)
    {
        try
        {
            await accountList.AddDebugSyntheticAccountsAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(
                "Debug synthetic account creation failed with {ExceptionType}.",
                exception.GetType().FullName);
        }
    }

    private static async Task DeleteDebugSyntheticAccountsAsync(AccountListViewModel accountList)
    {
        try
        {
            await accountList.DeleteDebugSyntheticAccountsAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(
                "Debug synthetic account deletion failed with {ExceptionType}.",
                exception.GetType().FullName);
        }
    }

    private static async Task ImportDebugAccountsAsync(
        AccountListViewModel accountList,
        string? filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath)
                || !Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(filePath))
            {
                return;
            }

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            await accountList.ImportAccountsAsync(stream, Path.GetFileName(filePath));
        }
        catch (Exception exception)
        {
            Log.Warning(
                "Debug account import failed with {ExceptionType}.",
                exception.GetType().FullName);
        }
    }

    private static async Task DeleteAllAccountsAsync(AccountListViewModel accountList)
    {
        try
        {
            await accountList.DeleteAllAccountsAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(
                "Debug deletion of all accounts failed with {ExceptionType}.",
                exception.GetType().FullName);
        }
    }
#endif

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
