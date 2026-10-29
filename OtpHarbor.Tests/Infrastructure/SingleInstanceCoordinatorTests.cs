using TOTP.Core.Platform;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure;

public sealed class SingleInstanceCoordinatorTests
{
    [Theory]
    [InlineData(InstanceLockAcquireResult.Acquired, SingleInstanceOutcome.Primary)]
    [InlineData(InstanceLockAcquireResult.Recovered, SingleInstanceOutcome.RecoveredPrimary)]
    public void Start_WhenLockIsAvailable_BecomesPrimary(
        InstanceLockAcquireResult lockResult,
        SingleInstanceOutcome expected)
    {
        var instanceLock = new FakeInstanceLock(lockResult);
        var dispatcher = new FakeActivationDispatcher(true);
        using var sut = new SingleInstanceCoordinator(instanceLock, dispatcher);

        var outcome = sut.Start(ApplicationActivationRequest.ActivateMainWindow());

        Assert.Equal(expected, outcome);
        Assert.Null(dispatcher.Request);
    }

    [Theory]
    [InlineData(true, SingleInstanceOutcome.ActivationRedirected)]
    [InlineData(false, SingleInstanceOutcome.ActivationFailed)]
    public void Start_WhenAnotherInstanceOwnsLock_DispatchesPortableActivation(
        bool dispatchResult,
        SingleInstanceOutcome expected)
    {
        var request = ApplicationActivationRequest.ActivateMainWindow();
        var dispatcher = new FakeActivationDispatcher(dispatchResult);
        using var sut = new SingleInstanceCoordinator(
            new FakeInstanceLock(InstanceLockAcquireResult.AlreadyRunning),
            dispatcher);

        var outcome = sut.Start(request);

        Assert.Equal(expected, outcome);
        Assert.Equal(request, dispatcher.Request);
    }

    [Fact]
    public void Start_WhenCalledTwice_RejectsAmbiguousOwnership()
    {
        using var sut = new SingleInstanceCoordinator(
            new FakeInstanceLock(InstanceLockAcquireResult.Acquired),
            new FakeActivationDispatcher(true));
        sut.Start(ApplicationActivationRequest.ActivateMainWindow());

        Assert.Throws<InvalidOperationException>(() =>
            sut.Start(ApplicationActivationRequest.ActivateMainWindow()));
    }

    [Fact]
    public void Start_WhenPayloadVersionIsUnsupported_DoesNotAcquireLock()
    {
        var instanceLock = new FakeInstanceLock(InstanceLockAcquireResult.Acquired);
        using var sut = new SingleInstanceCoordinator(instanceLock, new FakeActivationDispatcher(true));

        Assert.Throws<ArgumentException>(() =>
            sut.Start(new ApplicationActivationRequest(99, ApplicationActivationKind.ActivateMainWindow)));
        Assert.Equal(0, instanceLock.AcquireCalls);
    }

    [Fact]
    public void Dispose_ReleasesPlatformLockOnce()
    {
        var instanceLock = new FakeInstanceLock(InstanceLockAcquireResult.Acquired);
        var sut = new SingleInstanceCoordinator(instanceLock, new FakeActivationDispatcher(true));

        sut.Dispose();
        sut.Dispose();

        Assert.Equal(1, instanceLock.DisposeCalls);
    }

    private sealed class FakeInstanceLock(InstanceLockAcquireResult result) : IInstanceLock
    {
        public int AcquireCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public InstanceLockAcquireResult Acquire()
        {
            AcquireCalls++;
            return result;
        }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class FakeActivationDispatcher(bool result) : IActivationDispatcher
    {
        public ApplicationActivationRequest? Request { get; private set; }

        public bool TryDispatch(ApplicationActivationRequest request)
        {
            Request = request;
            return result;
        }
    }
}
