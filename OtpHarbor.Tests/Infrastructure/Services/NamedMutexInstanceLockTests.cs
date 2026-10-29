using TOTP.Core.Platform;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class NamedMutexInstanceLockTests
{
    [Fact]
    public async Task Acquire_BlocksAnotherThreadUntilOwnerDisposes()
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
        Assert.Contains(
            await acquired.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken),
            new[] { InstanceLockAcquireResult.Acquired, InstanceLockAcquireResult.Recovered });

        try
        {
            using var secondary = new NamedMutexInstanceLock(name);
            Assert.Equal(InstanceLockAcquireResult.AlreadyRunning, secondary.Acquire());
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
