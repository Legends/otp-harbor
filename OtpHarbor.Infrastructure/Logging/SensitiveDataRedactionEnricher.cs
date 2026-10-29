using Serilog.Core;
using Serilog.Events;

namespace TOTP.Infrastructure.Logging;

/// <summary>
/// Redacts sensitive log properties before they are written to sinks.
/// </summary>
public sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    private static readonly string[] SensitiveMarkers =
    [
        "password",
        "passwd",
        "pwd",
        "passphrase",
        "secret",
        "seed",
        "token",
        "credential",
        "apikey",
        "clientsecret",
        "masterpassword",
        "wrappeddek",
        "dek",
        "keymaterial",
        "pfxpassword"
    ];

    private const string Redacted = "[REDACTED]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToArray())
        {
            var sanitized = SanitizeValue(property.Key, property.Value);
            logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, sanitized));
        }
    }

    private static LogEventPropertyValue SanitizeValue(string propertyName, LogEventPropertyValue value)
    {
        if (IsSensitiveProperty(propertyName))
        {
            return new ScalarValue(Redacted);
        }

        return value switch
        {
            StructureValue structure => new StructureValue(
                structure.Properties.Select(p => new LogEventProperty(p.Name, SanitizeValue(p.Name, p.Value))),
                structure.TypeTag),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(e => SanitizeValue(propertyName, e))),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(kvp =>
            {
                string keyName = kvp.Key.Value?.ToString() ?? string.Empty;
                return new KeyValuePair<ScalarValue, LogEventPropertyValue>(kvp.Key, SanitizeValue(keyName, kvp.Value));
            })),
            _ => value
        };
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var normalized = propertyName
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }
}
