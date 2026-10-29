using TOTP.Core.Platform;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Unix.Infrastructure;

public sealed class SingleInstanceTransportTests
{
    [Fact]
    public async Task NamedPipe_RoundTripsActivationWithinCurrentUser()
    {
        var pipeName = $"totp-test-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<ApplicationActivationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new NamedPipeActivationListener(pipeName);
        listener.Start(request => received.TrySetResult(request));

        Assert.True(new NamedPipeActivationDispatcher(pipeName)
            .TryDispatch(ApplicationActivationRequest.ActivateMainWindow()));
        var request = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationActivationKind.ActivateMainWindow, request.Kind);
    }

    [Fact]
    public async Task NamedMutex_BlocksAnotherThread()
    {
        var name = $"totp-test-{Guid.NewGuid():N}";
        var acquired = new TaskCompletionSource<InstanceLockAcquireResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryTask = Task.Run(
            () =>
            {
                using var primary = new NamedMutexInstanceLock(name);
                acquired.SetResult(primary.Acquire());
                release.Task.GetAwaiter().GetResult();
            },
            TestContext.Current.CancellationToken);
        Assert.NotEqual(
            InstanceLockAcquireResult.AlreadyRunning,
            await acquired.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));

        try
        {
            using var secondaryLock = new NamedMutexInstanceLock(name);
            Assert.Equal(InstanceLockAcquireResult.AlreadyRunning, secondaryLock.Acquire());
        }
        finally
        {
            release.TrySetResult();
            await primaryTask.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        }
    }
}
