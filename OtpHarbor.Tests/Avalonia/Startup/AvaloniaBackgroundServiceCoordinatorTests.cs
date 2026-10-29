using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Avalonia.Desktop.Startup;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaBackgroundServiceCoordinatorTests
{
    [Fact]
    public async Task StartAndStop_DoNotWaitForAsyncContinuationsOnCallingContext()
    {
        var service = new ContextCapturingHostedService();
        var sut = new AvaloniaBackgroundServiceCoordinator(
            [service],
            Mock.Of<ILogger<AvaloniaBackgroundServiceCoordinator>>());

        await Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            sut.Start();
            sut.Stop();
        }, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(service.Started);
        Assert.True(service.Stopped);
    }

    private sealed class ContextCapturingHostedService : IHostedService
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            Started = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            Stopped = true;
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }
}
