using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Security.Interfaces;
using AppLifetime = TOTP.Core.Services.Interfaces.IApplicationLifetime;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaExceptionBoundaryTests
{
    [Fact]
    public void TryHandleUiThread_LocksAndRequestsFatalShutdownWithoutLoggingMessage()
    {
        var authorization = new Mock<IAuthorizationService>();
        var lifetime = new Mock<AppLifetime>();
        var logger = new RecordingLogger<AvaloniaExceptionBoundary>();
        var sut = new AvaloniaExceptionBoundary(
            authorization.Object,
            lifetime.Object,
            logger);

        var handled = sut.TryHandleUiThread(
            new InvalidOperationException("synthetic secret-bearing detail"));

        Assert.True(handled);
        authorization.Verify(value => value.Lock(), Times.Once);
        lifetime.Verify(value => value.Shutdown(1), Times.Once);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("secret-bearing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                typeof(InvalidOperationException).FullName!,
                StringComparison.Ordinal));
        Assert.All(logger.Exceptions, Assert.Null);
    }

    [Fact]
    public void TryHandleUiThread_WhenShutdownFails_ReturnsFalseAndRedactsBothMessages()
    {
        var authorization = new Mock<IAuthorizationService>();
        var lifetime = new Mock<AppLifetime>();
        lifetime.Setup(value => value.Shutdown(1))
            .Throws(new IOException("private local path"));
        var logger = new RecordingLogger<AvaloniaExceptionBoundary>();
        var sut = new AvaloniaExceptionBoundary(
            authorization.Object,
            lifetime.Object,
            logger);

        var handled = sut.TryHandleUiThread(
            new InvalidOperationException("sensitive input"));

        Assert.False(handled);
        authorization.Verify(value => value.Lock(), Times.Once);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("sensitive", StringComparison.OrdinalIgnoreCase)
                || message.Contains("private", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logger.Messages,
            message => message.Contains(typeof(IOException).FullName!, StringComparison.Ordinal));
        Assert.All(logger.Exceptions, Assert.Null);
    }

    [Fact]
    public void HandleDomain_WhenLockFails_LogsTypesOnlyAndDoesNotEscape()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(value => value.Lock())
            .Throws(new InvalidOperationException("authorization internals"));
        var logger = new RecordingLogger<AvaloniaExceptionBoundary>();
        var sut = new AvaloniaExceptionBoundary(
            authorization.Object,
            Mock.Of<AppLifetime>(),
            logger);

        var act = () => sut.HandleDomain(
            new IOException("secret file location"),
            isTerminating: true);

        var exception = Record.Exception(act);
        Assert.Null(exception);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || message.Contains("internals", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logger.Messages,
            message => message.Contains(typeof(IOException).FullName!, StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                typeof(InvalidOperationException).FullName!,
                StringComparison.Ordinal));
    }

    [Fact]
    public void HandleUnobservedTask_DoesNotLockOrShutDown()
    {
        var authorization = new Mock<IAuthorizationService>();
        var lifetime = new Mock<AppLifetime>();
        var logger = new RecordingLogger<AvaloniaExceptionBoundary>();
        var sut = new AvaloniaExceptionBoundary(
            authorization.Object,
            lifetime.Object,
            logger);

        sut.HandleUnobservedTask(new ApplicationException("background detail"));

        authorization.Verify(value => value.Lock(), Times.Never);
        lifetime.Verify(value => value.Shutdown(It.IsAny<int>()), Times.Never);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("background detail", StringComparison.Ordinal));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
