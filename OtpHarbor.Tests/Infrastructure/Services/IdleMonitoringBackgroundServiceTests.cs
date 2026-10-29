using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class IdleMonitoringBackgroundServiceTests
{
    [Fact]
    public void EvaluateIdlePolicy_WhenUnlockedAndTimeoutReached_LocksAndNotifies()
    {
        var context = CreateContext(TimeSpan.FromMinutes(10));
        var notifications = 0;
        context.Service.ApplicationLocked += (_, _) => notifications++;

        context.Service.EvaluateIdlePolicy();
        context.Time.Advance(TimeSpan.FromMinutes(10));
        context.Service.EvaluateIdlePolicy();

        context.Authorization.Verify(value => value.Lock(), Times.Once);
        Assert.Equal(1, notifications);
        Assert.False(context.State.IsUnlocked);
    }

    [Fact]
    public void RecordActivity_BeforeTimeout_RestartsIdleWindow()
    {
        var context = CreateContext(TimeSpan.FromMinutes(10));

        context.Service.EvaluateIdlePolicy();
        context.Time.Advance(TimeSpan.FromMinutes(9));
        context.Service.RecordActivity();
        context.Time.Advance(TimeSpan.FromMinutes(9));
        context.Service.EvaluateIdlePolicy();

        context.Authorization.Verify(value => value.Lock(), Times.Never);

        context.Time.Advance(TimeSpan.FromMinutes(1));
        context.Service.EvaluateIdlePolicy();
        context.Authorization.Verify(value => value.Lock(), Times.Once);
    }

    [Fact]
    public void EvaluateIdlePolicy_WhenIdleTimeoutDisabled_DoesNotLock()
    {
        var context = CreateContext(TimeSpan.Zero);

        context.Service.EvaluateIdlePolicy();
        context.Time.Advance(TimeSpan.FromDays(1));
        context.Service.EvaluateIdlePolicy();

        context.Authorization.Verify(value => value.Lock(), Times.Never);
    }

    private static TestContext CreateContext(TimeSpan timeout)
    {
        var state = new AuthorizationState();
        state.Unlock();
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.Lock()).Callback(state.Lock);
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings
        {
            IdleTimeout = timeout
        });
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var service = new IdleMonitoringBackgroundService(
            authorization.Object,
            settings.Object,
            Mock.Of<ILogger<IdleMonitoringBackgroundService>>(),
            time);
        return new TestContext(service, authorization, state, time);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
            _timestamp += duration.Ticks;
        }
    }

    private sealed record TestContext(
        IdleMonitoringBackgroundService Service,
        Mock<IAuthorizationService> Authorization,
        AuthorizationState State,
        ManualTimeProvider Time);
}
