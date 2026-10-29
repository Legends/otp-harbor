using Serilog;
using Serilog.Core;
using Serilog.Events;
using TOTP.Infrastructure.Logging;

namespace TOTP.Tests.Services;

public sealed class SensitiveDataRedactionEnricherTests
{
    [Fact]
    public void Enricher_RedactsTopLevelSensitiveProperties()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new SensitiveDataRedactionEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Auth {Password} {Issuer}", "super-secret", "GitHub");

        var evt = Assert.Single(sink.Events);
        Assert.Equal("[REDACTED]", GetScalarString(evt.Properties["Password"]));
        Assert.Equal("GitHub", GetScalarString(evt.Properties["Issuer"]));
    }

    [Fact]
    public void Enricher_RedactsNestedSensitiveProperties()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new SensitiveDataRedactionEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Payload {@Payload}", new
        {
            Issuer = "GitHub",
            Secret = "AAAA-BBBB",
            AccessToken = "token-123"
        });

        var evt = Assert.Single(sink.Events);
        var payload = Assert.IsType<StructureValue>(evt.Properties["Payload"]);
        var props = payload.Properties.ToDictionary(p => p.Name, p => p.Value);

        Assert.Equal("GitHub", GetScalarString(props["Issuer"]));
        Assert.Equal("[REDACTED]", GetScalarString(props["Secret"]));
        Assert.Equal("[REDACTED]", GetScalarString(props["AccessToken"]));
    }

    private static string? GetScalarString(LogEventPropertyValue value) =>
        Assert.IsType<ScalarValue>(value).Value?.ToString();

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
