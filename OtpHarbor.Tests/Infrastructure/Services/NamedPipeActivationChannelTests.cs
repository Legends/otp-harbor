using TOTP.Core.Platform;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class NamedPipeActivationChannelTests
{
    [Fact]
    public async Task Dispatcher_SendsSupportedRequestToCurrentUserListener()
    {
        var pipeName = $"totp-test-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<ApplicationActivationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new NamedPipeActivationListener(pipeName);
        listener.Start(request => received.TrySetResult(request));
        var dispatcher = new NamedPipeActivationDispatcher(pipeName);

        var dispatched = dispatcher.TryDispatch(ApplicationActivationRequest.ActivateMainWindow());
        var request = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(dispatched);
        Assert.Equal(ApplicationActivationKind.ActivateMainWindow, request.Kind);
        Assert.Equal(ApplicationActivationRequest.CurrentVersion, request.Version);
    }

    [Fact]
    public async Task Dispatcher_PreservesBoundedActivationPayload()
    {
        var pipeName = $"totp-test-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<ApplicationActivationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new NamedPipeActivationListener(pipeName);
        listener.Start(request => received.TrySetResult(request));
        var dispatcher = new NamedPipeActivationDispatcher(pipeName);
        var expected = ApplicationActivationRequest.DebugImportAccounts(
            Path.Combine(Path.GetTempPath(), "accounts.json"));

        var dispatched = dispatcher.TryDispatch(expected);
        var request = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(dispatched);
        Assert.Equal(expected, request);
    }

    [Fact]
    public void Dispatcher_WhenNoListenerExists_FailsClosed()
    {
        var dispatcher = new NamedPipeActivationDispatcher($"totp-test-{Guid.NewGuid():N}");

        Assert.False(dispatcher.TryDispatch(ApplicationActivationRequest.ActivateMainWindow()));
    }
}
