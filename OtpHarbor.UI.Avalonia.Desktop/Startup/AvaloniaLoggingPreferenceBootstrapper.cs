using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Infrastructure.Logging;

namespace TOTP.Avalonia.Desktop.Startup;

public static class AvaloniaLoggingPreferenceBootstrapper
{
    public static bool ApplyFromPreferences(
        IAppPreferencesStore preferencesStore,
        AppLogLevel? commandLineOverride)
    {
        ArgumentNullException.ThrowIfNull(preferencesStore);

        if (commandLineOverride.HasValue) return false;

        var result = preferencesStore.LoadAsync().GetAwaiter().GetResult();
        if (result.IsFailed || result.Value is null) return false;

        var level = Enum.IsDefined(result.Value.MinimumLogLevel)
            ? result.Value.MinimumLogLevel
            : AppLogLevel.Information;
        LogSwitchService.SharedSwitch.MinimumLevel = level.ToSerilogLevel();
        return true;
    }
}
