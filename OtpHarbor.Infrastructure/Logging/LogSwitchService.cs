
using Serilog.Core;
using Serilog.Events;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Logging;

/*
 *  Level	    Value	Usage
    ----------------------------------------------------------------
    Verbose	    0	    Everything. Absolute "firehose" of data.
    Debug	    1	    Internal control flow and state changes.
    Information	2	    General app flow (e.g., "Service Started").
    Warning	    3	    Handled errors or suspicious behavior.
    Error	    4	    Crashes/Exceptions that affect a feature.
    Fatal	    5	    Total app failure. App must shut down.
 */

/// <summary>
/// Provides centralized control over the application's logging level, allowing dynamic adjustment of log verbosity at
/// runtime.
/// </summary>
/// <remarks>LogSwitchService exposes a shared logging level switch that can be used to coordinate log filtering
/// across multiple components. Changing the logging level affects which log events are emitted, ranging from verbose
/// diagnostic information to critical errors. This service is intended for scenarios where consistent log level
/// management is required throughout the application.</remarks>
public class LogSwitchService : ILogSwitchService
{
    // This stays internal to the Infrastructure/Logging setup
    public static LoggingLevelSwitch SharedSwitch { get; } = new();

    public bool IsCliOverrideActive { get; set; }

    public LogSwitchService(AppLogLevel initialLevel, bool isCliOverride)
    {
        IsCliOverrideActive = isCliOverride;
        // Fix CS0029 by using the Mapper extension
        SharedSwitch.MinimumLevel = initialLevel.ToSerilogLevel();
    }

    public AppLogLevel MinimumLevel => SharedSwitch.MinimumLevel.ToAppLevel();

    public void SetLevel(AppLogLevel level)
    {
        // Explicitly map the Core enum to the Serilog enum
        SharedSwitch.MinimumLevel = level.ToSerilogLevel();
    }

    public AppLogLevel GetLevel()
    {
        // Map back from Serilog to Core
        return SharedSwitch.MinimumLevel.ToAppLevel();
    }
}




