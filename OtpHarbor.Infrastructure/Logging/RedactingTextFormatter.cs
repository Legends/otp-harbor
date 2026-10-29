using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using System.Globalization;

namespace TOTP.Infrastructure.Logging;

public sealed class RedactingTextFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner;

    public RedactingTextFormatter(string outputTemplate)
    {
        _inner = new MessageTemplateTextFormatter(outputTemplate, CultureInfo.InvariantCulture);
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        _inner.Format(logEvent, writer);
        var raw = writer.ToString();
        var sanitized = SensitiveTextRedactor.Sanitize(raw);
        output.Write(sanitized);
    }
}
