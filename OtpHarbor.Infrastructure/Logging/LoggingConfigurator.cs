using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Logging;

/// <summary>
/// Provides methods and properties for configuring application logging, including support for command-line log level
/// overrides and integration with host-based logging setups.
/// </summary>
public static class LoggingConfigurator
{
    private const string LogOutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
    private static readonly SensitiveDataRedactionEnricher RedactionEnricher = new();
    private static readonly RedactingTextFormatter RedactingTextFormatter = new(LogOutputTemplate);

    // Allows other services to check if the user is forcing a specific log level
    public static AppLogLevel? ManualOverrideLevel { get; private set; }

    private static readonly Dictionary<string, AppLogLevel> _levelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "-v", AppLogLevel.Verbose },     { "--verbose", AppLogLevel.Verbose },
        { "-d", AppLogLevel.Debug },       { "--debug", AppLogLevel.Debug },
        { "-i", AppLogLevel.Information }, { "--info", AppLogLevel.Information },
        { "-w", AppLogLevel.Warning },     { "--warn", AppLogLevel.Warning },
        { "-e", AppLogLevel.Error },       { "--error", AppLogLevel.Error },
        { "-f", AppLogLevel.Fatal },       { "--fatal", AppLogLevel.Fatal }
    };

    public static AppLogLevel? GetLevelFromArgs(string[] args)
    {
        if (args == null || args.Length == 0) return null;

        foreach (var arg in args)
        {
            if (_levelMap.TryGetValue(arg, out var level))
            {
                return level;
            }
        }
        return null;
    }

    /// <summary>
    /// Initializes a bootstrap logger to catch early startup errors before the DI container is ready.
    /// </summary>
    public static void SetupEarlyLogger(string[] args, IPlatformApplicationPaths applicationPaths)
    {
        var levelFromArgs = GetLevelFromArgs(args);
        if (levelFromArgs.HasValue)
        {
            ManualOverrideLevel = levelFromArgs;
            // Use our mapping extension to update the switch
            LogSwitchService.SharedSwitch.MinimumLevel = levelFromArgs.Value.ToSerilogLevel();
        }

        Directory.CreateDirectory(applicationPaths.LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LogSwitchService.SharedSwitch)
            .Enrich.FromLogContext()
            .Enrich.With(RedactionEnricher)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Debug(RedactingTextFormatter) // Essential for seeing logs in Visual Studio Output window
            .WriteTo.Async(a => a.File(
                RedactingTextFormatter,
                applicationPaths.LogFilePath,
                rollingInterval: RollingInterval.Day,
                shared: true))
            .CreateBootstrapLogger();

        if (ManualOverrideLevel.HasValue)
        {
            Log.Information("Early Logger initialized with CLI override: {Level}", ManualOverrideLevel.Value);
        }
    }

    /// <summary>
    /// Configures the final logger when the Host is ready, integrating appsettings.json and the SharedSwitch.
    /// </summary>
    public static void ConfigureWithHostContext(HostBuilderContext context, IServiceProvider services, LoggerConfiguration config)
    {
        var applicationPaths = services.GetRequiredService<IPlatformApplicationPaths>();
        Directory.CreateDirectory(applicationPaths.LogDirectory);

        config
            .ReadFrom.Configuration(context.Configuration) // 1. Load basic settings from appsettings.json
            .ReadFrom.Services(services)                   // 2. Allow Serilog to access DI services
            .MinimumLevel.ControlledBy(LogSwitchService.SharedSwitch) // 3. Re-attach the master switch for runtime changes
            .Enrich.FromLogContext()
            .Enrich.With(RedactionEnricher)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(RedactingTextFormatter)
            .WriteTo.Debug(RedactingTextFormatter)
            .WriteTo.Async(a => a.File(
                RedactingTextFormatter,
                applicationPaths.LogFilePath,
                rollingInterval: RollingInterval.Day,
                shared: true));

        if (ManualOverrideLevel.HasValue)
        {
            // Re-apply the override in case appsettings.json tried to change it
            LogSwitchService.SharedSwitch.MinimumLevel = ManualOverrideLevel.Value.ToSerilogLevel();
            Log.Write(ManualOverrideLevel.Value.ToSerilogLevel(), "Host logger configured. Maintaining CLI override: {Level}", ManualOverrideLevel);
        }
    }

    /// <summary>
    /// Standard shutdown to ensure async buffers are flushed to disk.
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }
}
