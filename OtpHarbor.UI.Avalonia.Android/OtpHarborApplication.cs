using Android.App;
using Android.Content;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TOTP.Avalonia.Mobile;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Mobile.Views;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Logging;

namespace TOTP.Avalonia.Android;

[Application(AllowBackup = false, UsesCleartextTraffic = false)]
public class OtpHarborApplication : AvaloniaAndroidApplication<MobileApp>
{
    private ServiceProvider? _services;
    private NativeBrandIconCache? _brandIconCache;
    private MainActivity? _mainActivity;
    private bool _startupSurfaceReady;

    protected OtpHarborApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        AndroidEnvironment.UnhandledExceptionRaiser += AndroidUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += DomainUnhandledException;
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).AfterSetup(_ =>
        {
            try
            {
                _services = AndroidCompositionRoot.Build(this);
                var brandIconCache = _brandIconCache ??= new NativeBrandIconCache();
                var viewModel = _services.GetRequiredService<MobileShellViewModel>();
                var app = global::Avalonia.Application.Current as MobileApp
                    ?? throw new InvalidOperationException("The mobile Avalonia application is unavailable.");
                app.ConfigureAppearance(_services.GetRequiredService<IAppearanceSettingsService>());
                app.MainViewFactory = () =>
                {
                    viewModel.UseNativeDeleteConfirmation();
                    if (!_startupSurfaceReady)
                        viewModel.DeferAutomaticBiometricUnlockUntilStartupSurfaceReady();
                    var view = new MainView { DataContext = viewModel };
                    EventHandler? firstLayout = null;
                    firstLayout = (_, _) =>
                    {
                        var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(view);
                        if (topLevel is null || view.Bounds.Width <= 0 || view.Bounds.Height <= 0)
                            return;
                        view.LayoutUpdated -= firstLayout;
                        // Layout alone does not mean that the renderer has submitted a frame.
                        // Keep the native surface across the first rendering opportunity.
                        topLevel.RequestAnimationFrame(_ =>
                            topLevel.RequestAnimationFrame(_ => NotifyStartupSurfaceReady()));
                    };
                    view.LayoutUpdated += firstLayout;
                    var accountListHost = new AndroidAccountListHost(
                        viewModel,
                        brandIconCache,
                        view.DismissAccountSearchFocus,
                        view.NotifyNativeAccountListRendered);
                    var accountGroupStripHost = new AndroidAccountGroupStripHost(
                        viewModel,
                        view.DismissAccountSearchFocus);
                    view.StartupContentRevealRequested += (_, _) =>
                    {
                        accountListHost.CompleteStartupTransition();
                        accountGroupStripHost.CompleteStartupTransition();
                    };
                    view.UseNativeAccountList(accountListHost);
                    view.UseNativeImportReviewList(new AndroidImportReviewHost(viewModel));
                    view.UseNativeAccountGroupStrip(accountGroupStripHost);
                    view.UseNativeGroupAccountPicker(new AndroidGroupAccountPickerHost(
                        viewModel,
                        brandIconCache));
                    viewModel.InitializeCommand.Execute(null);
                    return view;
                };
            }
            catch (Exception exception)
            {
                Log.Fatal(
                    exception,
                    "Android application startup failed with exception type {ExceptionType}.",
                    exception.GetType().FullName);
                global::Android.Util.Log.Error(
                    "OtpHarborStartup",
                    SensitiveTextRedactor.Sanitize(exception.ToString()));
                throw;
            }
        });
    }

    public void NotifyEnteredBackground(bool lockImmediately)
    {
        Log.Debug("Android application entered the background.");
        _services?.GetService<IMobileLifecycleSink>()?.OnEnteredBackground(lockImmediately);
    }

    internal bool IsScreenCaptureProtectionRequired =>
        _services?.GetService<MobileShellViewModel>()?.IsScreenCaptureProtectionRequired != false;

#if DEBUG
    internal Task<bool> ShowOnboardingPreviewAsync() => Task.FromResult(
        _services?.GetService<MobileShellViewModel>()?.ShowOnboardingPreview() ?? false);

    internal async Task<bool> SetDebugThemeAsync(string? theme)
    {
        var preference = theme?.ToLowerInvariant() switch
        {
            "system" => AppThemePreference.System,
            "dark" => AppThemePreference.Dark,
            "light" => AppThemePreference.Light,
            _ => (AppThemePreference?)null
        };
        var appearance = _services?.GetService<IAppearanceSettingsService>();
        if (preference is null || appearance is null) return false;

        var result = await appearance.SetThemePreferenceAsync(preference.Value);
        return result.IsSuccess;
    }

    internal Task<bool> AddDebugSyntheticAccountsAsync(int count) =>
        _services?.GetService<MobileShellViewModel>()?.AddDebugSyntheticAccountsAsync(count)
        ?? Task.FromResult(false);

    internal Task<bool> DeleteDebugSyntheticAccountsAsync() =>
        _services?.GetService<MobileShellViewModel>()?.DeleteDebugSyntheticAccountsAsync()
        ?? Task.FromResult(false);

    internal async Task<bool> ImportDebugAccountsAsync(Context context, string? filePath)
    {
        var viewModel = _services?.GetService<MobileShellViewModel>();
        if (viewModel is null || string.IsNullOrWhiteSpace(filePath)) return false;

        var cacheDirectory = Path.GetFullPath(context.CacheDir?.AbsolutePath ?? string.Empty);
        var resolvedPath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(cacheDirectory, resolvedPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || !Path.GetExtension(resolvedPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await using var stream = File.OpenRead(resolvedPath);
        return await viewModel.ImportAccountsAsync(stream, Path.GetFileName(resolvedPath));
    }

    internal Task<bool> DeleteAllAccountsAsync() =>
        _services?.GetService<MobileShellViewModel>()?.DeleteAllAccountsAsync()
        ?? Task.FromResult(false);

    internal async Task<bool> ImportDebugIconPackAsync(Context context, string? filePath)
    {
        var iconPacks = _services?.GetService<IBrandIconPackService>();
        if (iconPacks is null || string.IsNullOrWhiteSpace(filePath)) return false;
        var cacheDirectory = Path.GetFullPath(context.CacheDir?.AbsolutePath ?? string.Empty);
        var resolvedPath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(cacheDirectory, resolvedPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || !Path.GetExtension(resolvedPath).Equals(".otphicons", StringComparison.OrdinalIgnoreCase))
            return false;
        await using var stream = File.OpenRead(resolvedPath);
        var imported = await iconPacks.ImportAsync(stream, Path.GetFileName(resolvedPath));
        return imported.IsSuccess;
    }
#endif

    public void AttachActivity(MainActivity activity)
    {
        _mainActivity = activity;
        if (_startupSurfaceReady)
            activity.ReleaseStartupSplash();
        _services?.GetService<AndroidActivityProvider>()?.Attach(activity);
        var viewModel = _services?.GetService<MobileShellViewModel>();
        if (viewModel is not null)
            activity.AttachScreenCapturePolicy(viewModel);
    }

    public void DetachActivity(MainActivity activity)
    {
        activity.DetachScreenCapturePolicy();
        _services?.GetService<AndroidActivityProvider>()?.Detach(activity);
        if (ReferenceEquals(_mainActivity, activity))
            _mainActivity = null;
    }

    private void NotifyStartupSurfaceReady()
    {
        _startupSurfaceReady = true;
        _mainActivity?.ReleaseStartupSplash();
    }

    internal void NotifyStartupSplashRemoved(MainActivity activity)
    {
        if (_startupSurfaceReady && ReferenceEquals(_mainActivity, activity))
            _services?.GetService<MobileShellViewModel>()?.NotifyStartupSurfaceReady();
    }

    public void NotifyReturnedToForeground()
    {
        Log.Debug("Android application returned to the foreground.");
        _services?.GetService<IMobileLifecycleSink>()?.OnReturnedToForeground();
    }

    public override void OnTerminate()
    {
        AndroidEnvironment.UnhandledExceptionRaiser -= AndroidUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= DomainUnhandledException;
        (global::Avalonia.Application.Current as MobileApp)?.DisposeAppearance();
        _brandIconCache?.Dispose();
        _brandIconCache = null;
        _services?.Dispose();
        _services = null;
        LoggingConfigurator.Shutdown();
        base.OnTerminate();
    }

    private static void AndroidUnhandledException(
        object? sender,
        RaiseThrowableEventArgs args) =>
        RecordFatalException("Android runtime", args.Exception);

    private static void DomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs args) =>
        RecordFatalException("managed application domain", args.ExceptionObject as Exception);

    private static void RecordFatalException(string boundary, Exception? exception)
    {
        var exceptionType = exception?.GetType().FullName ?? "Unknown";
        var sanitizedDiagnostic = SensitiveTextRedactor.Sanitize(
            exception?.ToString() ?? "Unknown unhandled exception");
        Log.Fatal(
            "Unhandled exception reached the {Boundary} boundary with type {ExceptionType}. "
            + "Diagnostic: {Diagnostic}",
            boundary,
            exceptionType,
            sanitizedDiagnostic);
        global::Android.Util.Log.Error(
            "OtpHarborCrash",
            $"{boundary}: {sanitizedDiagnostic}");
        LoggingConfigurator.Shutdown();
    }
}
