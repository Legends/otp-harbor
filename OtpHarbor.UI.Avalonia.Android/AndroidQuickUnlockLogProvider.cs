using Microsoft.Extensions.Logging;
using AndroidLog = Android.Util.Log;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidQuickUnlockLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName.Contains("AndroidPlatformQuickUnlock", StringComparison.Ordinal)
            ? new LogcatLogger()
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class LogcatLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            AndroidLog.Warn("OTP-Harbor-QuickUnlock", formatter(state, null));
        }
    }
}
