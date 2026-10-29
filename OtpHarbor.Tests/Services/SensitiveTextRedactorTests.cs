using Serilog.Events;
using Serilog.Parsing;
using TOTP.Infrastructure.Logging;

namespace TOTP.Tests.Services;

public sealed class SensitiveTextRedactorTests
{
    [Theory]
    [InlineData("password=hunter2", "password=[REDACTED]")]
    [InlineData("secret:ABCDEF", "secret=[REDACTED]")]
    [InlineData("token = xyz", "token=[REDACTED]")]
    [InlineData("Bearer abc.def.ghi", "Bearer [REDACTED]")]
    [InlineData("https://x?a=1&secret=AAA&b=2", "https://x?a=1&secret=[REDACTED]&b=2")]
    [InlineData("{\"issuer\":\"Example\",\"secret\":\"JBSWY3DPEHPK3PXP\"}", "{\"issuer\":\"Example\",\"secret\":\"[REDACTED]\"}")]
    [InlineData("scan otpauth://totp/Issuer:user?secret=JBSWY3DPEHPK3PXP", "scan otpauth://[REDACTED]")]
    public void Sanitize_RedactsSensitiveFragments(string input, string expected)
    {
        var result = SensitiveTextRedactor.Sanitize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RedactingTextFormatter_RedactsExceptionAndMessageText()
    {
        var template = new MessageTemplateParser().Parse("{Message:lj}{NewLine}{Exception}");
        var evt = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            new InvalidOperationException("failed with password=hunter2"),
            template,
            [new LogEventProperty("Message", new ScalarValue("request token=abc123"))]);

        var formatter = new RedactingTextFormatter("{Message:lj}{NewLine}{Exception}");
        using var writer = new StringWriter();
        formatter.Format(evt, writer);
        var output = writer.ToString();

        Assert.DoesNotContain("hunter2", output, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", output, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", output, StringComparison.Ordinal);
    }
}
