using Serilog.Events;
using TOTP.Core.Enums;

namespace TOTP.Infrastructure.Logging;

/// <summary>
/// Provides mapping between application-internal log levels and Serilog-specific levels.
/// </summary>
public static class LogMappingExtensions
{
    public static LogEventLevel ToSerilogLevel(this AppLogLevel appLevel)
    {
        return appLevel switch
        {
            AppLogLevel.Verbose => LogEventLevel.Verbose,
            AppLogLevel.Debug => LogEventLevel.Debug,
            AppLogLevel.Information => LogEventLevel.Information,
            AppLogLevel.Warning => LogEventLevel.Warning,
            AppLogLevel.Error => LogEventLevel.Error,
            AppLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    public static AppLogLevel ToAppLevel(this LogEventLevel serilogLevel)
    {
        return serilogLevel switch
        {
            LogEventLevel.Verbose => AppLogLevel.Verbose,
            LogEventLevel.Debug => AppLogLevel.Debug,
            LogEventLevel.Information => AppLogLevel.Information,
            LogEventLevel.Warning => AppLogLevel.Warning,
            LogEventLevel.Error => AppLogLevel.Error,
            LogEventLevel.Fatal => AppLogLevel.Fatal,
            _ => AppLogLevel.Information
        };
    }
}