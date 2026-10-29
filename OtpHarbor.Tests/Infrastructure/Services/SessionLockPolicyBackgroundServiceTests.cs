using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Platform;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class SessionLockPolicyBackgroundServiceTests
{
    [Fact]
    public async Task SessionLocked_WhenEnabled_LocksApplication()
    {
        var context = CreateContext(lockOnSessionLock: true);
        var notifications = 0;
        context.Service.ApplicationLocked += (_, _) => notifications++;
        await context.Service.StartAsync(TestContext.Current.CancellationToken);

        context.Events.Raise(PlatformSessionState.Locked);

        context.Authorization.Verify(service => service.Lock(), Times.Once);
        Assert.Equal(1, notifications);
        await context.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SessionLocked_WhenDisabled_DoesNotLock()
    {
        var context = CreateContext(lockOnSessionLock: false);
        await context.Service.StartAsync(TestContext.Current.CancellationToken);

        context.Events.Raise(PlatformSessionState.Locked);

        context.Authorization.Verify(service => service.Lock(), Times.Never);
        await context.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SessionLocked_WhenApplicationIsAlreadyLocked_DoesNotReapplyLockedUiState()
    {
        var context = CreateContext(lockOnSessionLock: true, isUnlocked: false);
        var notifications = 0;
        context.Service.ApplicationLocked += (_, _) => notifications++;
        await context.Service.StartAsync(TestContext.Current.CancellationToken);

        context.Events.Raise(PlatformSessionState.Locked);

        context.Authorization.Verify(service => service.Lock(), Times.Never);
        Assert.Equal(0, notifications);
        await context.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(PlatformSessionState.Active)]
    [InlineData(PlatformSessionState.Disconnected)]
    [InlineData(PlatformSessionState.Unknown)]
    public async Task SessionNotLocked_DoesNotLock(PlatformSessionState state)
    {
        var context = CreateContext(lockOnSessionLock: true);
        await context.Service.StartAsync(TestContext.Current.CancellationToken);

        context.Events.Raise(state);

        context.Authorization.Verify(service => service.Lock(), Times.Never);
        await context.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SessionLocked_WhenLockThrows_LogsCritical()
    {
        var context = CreateContext(lockOnSessionLock: true);
        context.Authorization.Setup(service => service.Lock()).Throws(new InvalidOperationException("boom"));
        await context.Service.StartAsync(TestContext.Current.CancellationToken);

        context.Events.Raise(PlatformSessionState.Locked);

        VerifyLog(context.Logger, LogLevel.Critical, Times.Once());
        context.Logger.Verify(
            entry => entry.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.Is<Exception?>(exception => exception == null),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        await context.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HostedLifetime_StartsAndStopsPlatformEventSource()
    {
        var context = CreateContext(lockOnSessionLock: true);

        await context.Service.StartAsync(TestContext.Current.CancellationToken);
        await context.Service.StopAsync(TestContext.Current.CancellationToken);
        context.Events.Raise(PlatformSessionState.Locked);

        Assert.Equal(1, context.Events.StartCalls);
        Assert.Equal(1, context.Events.StopCalls);
        context.Authorization.Verify(service => service.Lock(), Times.Never);
    }

    private static TestContextData CreateContext(bool lockOnSessionLock, bool isUnlocked = true)
    {
        var events = new FakePlatformSessionEventSource();
        var authorization = new Mock<IAuthorizationService>();
        var authorizationState = new AuthorizationState();
        if (isUnlocked) authorizationState.Unlock();
        authorization.SetupGet(service => service.State).Returns(authorizationState);
        authorization.Setup(service => service.Lock()).Callback(authorizationState.Lock);
        var settings = new Mock<ISettingsService>();
        var logger = new Mock<ILogger<SessionLockPolicyBackgroundService>>();
        settings.SetupGet(service => service.Current).Returns(new AppSettings { LockOnSessionLock = lockOnSessionLock });

        var service = new SessionLockPolicyBackgroundService(
            events,
            authorization.Object,
            settings.Object,
            logger.Object);

        return new TestContextData(service, events, authorization, logger);
    }

    private static void VerifyLog(
        Mock<ILogger<SessionLockPolicyBackgroundService>> logger,
        LogLevel level,
        Times times) =>
        logger.Verify(
            entry => entry.Log(
                It.Is<LogLevel>(value => value == level),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private sealed record TestContextData(
        SessionLockPolicyBackgroundService Service,
        FakePlatformSessionEventSource Events,
        Mock<IAuthorizationService> Authorization,
        Mock<ILogger<SessionLockPolicyBackgroundService>> Logger);

    private sealed class FakePlatformSessionEventSource : IPlatformSessionEventSource
    {
        public bool IsSupported => true;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public event EventHandler<PlatformSessionChangedEventArgs>? SessionChanged;

        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;

        public void Raise(PlatformSessionState state) =>
            SessionChanged?.Invoke(this, new PlatformSessionChangedEventArgs(state));
    }
}
