using Avalonia;
using TOTP.Core.Platform;
using TOTP.Infrastructure.Services;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Camera.OpenCv;
using TOTP.Infrastructure.Logging;
using TOTP.DAL.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using System.Text.Json;

namespace TOTP.Avalonia.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--m3-native-probe"])
        {
            var probe = OpenCvNativeRuntimeProbe.Probe();
            if (!probe.IsAvailable)
                Console.Error.WriteLine($"OpenCV native probe failure: {probe.Failure}");
            Environment.ExitCode = probe.IsAvailable ? 0 : 2;
            return;
        }

        if (args is ["--m3-measurement-probe"])
        {
            var measurements = M3MeasurementProbe.Measure();
            Console.WriteLine(JsonSerializer.Serialize(measurements));
            Environment.ExitCode = measurements.NativeRuntimeAvailable ? 0 : 2;
            return;
        }

        try
        {
            var platformServices = DesktopPlatformServiceFactory.Create();
            LoggingConfigurator.SetupEarlyLogger(args, platformServices.ApplicationPaths);
            ApplyLoggingLevelPreference(platformServices);
            ApplyInterfaceScalePreference(platformServices);
            using var instance = new SingleInstanceCoordinator(
                new NamedMutexInstanceLock(DesktopInstanceIdentity.MutexName),
                new NamedPipeActivationDispatcher(DesktopInstanceIdentity.PipeName));
            var outcome = instance.Start(ApplicationActivationRequest.ActivateMainWindow());
            if (outcome == SingleInstanceOutcome.ActivationRedirected) return;
            if (outcome == SingleInstanceOutcome.ActivationFailed)
            {
                Environment.ExitCode = 1;
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(
                "Avalonia process boundary failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            Environment.ExitCode = 1;
        }
        finally
        {
            LoggingConfigurator.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();
#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder;
    }

    private static void ApplyInterfaceScalePreference(DesktopPlatformServices platformServices)
    {
        try
        {
            using var preferencesStore = new AppPreferencesStore(
                platformServices.ApplicationPaths.PreferencesFilePath,
                NullLogger<AppPreferencesStore>.Instance,
                platformServices.FileSecurity);
            if (AvaloniaInterfaceScaleBootstrapper.ApplyFromPreferences(preferencesStore))
                Log.Information("Applied the saved Avalonia interface-scale preference.");
        }
        catch (Exception exception)
        {
            Log.Warning(
                "The saved Avalonia interface scale could not be applied. Using system scaling. Exception type: {ExceptionType}.",
                exception.GetType().FullName);
        }
    }

    private static void ApplyLoggingLevelPreference(DesktopPlatformServices platformServices)
    {
        try
        {
            using var preferencesStore = new AppPreferencesStore(
                platformServices.ApplicationPaths.PreferencesFilePath,
                NullLogger<AppPreferencesStore>.Instance,
                platformServices.FileSecurity);
            if (AvaloniaLoggingPreferenceBootstrapper.ApplyFromPreferences(
                    preferencesStore,
                    LoggingConfigurator.ManualOverrideLevel))
            {
                Log.Information("Applied the saved minimum logging-level preference.");
            }
        }
        catch (Exception exception)
        {
            Log.Warning(
                "The saved minimum logging level could not be applied. Using the active default. Exception type: {ExceptionType}.",
                exception.GetType().FullName);
        }
    }
}
