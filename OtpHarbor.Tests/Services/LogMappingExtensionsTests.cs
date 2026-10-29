using Serilog.Events;
using TOTP.Core.Enums;
using TOTP.Infrastructure.Logging;

namespace TOTP.Tests.Services;

public sealed class LogMappingExtensionsTests
{
    [Theory]
    [InlineData(AppLogLevel.Verbose, LogEventLevel.Verbose)]
    [InlineData(AppLogLevel.Debug, LogEventLevel.Debug)]
    [InlineData(AppLogLevel.Information, LogEventLevel.Information)]
    [InlineData(AppLogLevel.Warning, LogEventLevel.Warning)]
    [InlineData(AppLogLevel.Error, LogEventLevel.Error)]
    [InlineData(AppLogLevel.Fatal, LogEventLevel.Fatal)]
    public void ToSerilogLevel_MapsKnownValues(AppLogLevel appLevel, LogEventLevel expected)
    {
        Assert.Equal(expected, appLevel.ToSerilogLevel());
    }

    [Fact]
    public void ToSerilogLevel_WhenUnknown_ReturnsInformation()
    {
        var result = ((AppLogLevel)999).ToSerilogLevel();
        Assert.Equal(LogEventLevel.Information, result);
    }

    [Theory]
    [InlineData(LogEventLevel.Verbose, AppLogLevel.Verbose)]
    [InlineData(LogEventLevel.Debug, AppLogLevel.Debug)]
    [InlineData(LogEventLevel.Information, AppLogLevel.Information)]
    [InlineData(LogEventLevel.Warning, AppLogLevel.Warning)]
    [InlineData(LogEventLevel.Error, AppLogLevel.Error)]
    [InlineData(LogEventLevel.Fatal, AppLogLevel.Fatal)]
    public void ToAppLevel_MapsKnownValues(LogEventLevel level, AppLogLevel expected)
    {
        Assert.Equal(expected, level.ToAppLevel());
    }
}
