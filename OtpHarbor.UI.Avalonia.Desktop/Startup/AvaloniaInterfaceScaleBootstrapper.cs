using System.Globalization;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;

namespace TOTP.Avalonia.Desktop.Startup;

public static class AvaloniaInterfaceScaleBootstrapper
{
    public const string EnvironmentVariableName = "AVALONIA_GLOBAL_SCALE_FACTOR";

    public static bool ApplyFromPreferences(IAppPreferencesStore preferencesStore)
    {
        ArgumentNullException.ThrowIfNull(preferencesStore);

        if (!OperatingSystem.IsLinux()
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName)))
        {
            return false;
        }

        var result = preferencesStore.LoadAsync().GetAwaiter().GetResult();
        if (result.IsFailed || result.Value is null) return false;

        var multiplier = ResolveMultiplier(result.Value);
        if (multiplier is null) return false;

        Environment.SetEnvironmentVariable(
            EnvironmentVariableName,
            multiplier.Value.ToString("0.##", CultureInfo.InvariantCulture));
        return true;
    }

    public static double? ResolveMultiplier(AppPreferencesV1 preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return !AppSettings.IsSupportedInterfaceScale(preferences.InterfaceScalePercent)
            || preferences.InterfaceScalePercent == AppSettings.DefaultInterfaceScalePercent
            ? null
            : preferences.InterfaceScalePercent / 100d;
    }
}
