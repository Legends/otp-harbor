using TOTP.Core.Platform;

namespace TOTP.Infrastructure.Services;

public sealed class SingleInstanceCoordinator(
    IInstanceLock instanceLock,
    IActivationDispatcher activationDispatcher) : IDisposable
{
    private bool _started;

    public SingleInstanceOutcome Start(ApplicationActivationRequest activationRequest)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Single-instance coordination has already started.");
        }

        if (!activationRequest.IsSupported)
        {
            throw new ArgumentException("The activation request is not supported.", nameof(activationRequest));
        }

        _started = true;
        return instanceLock.Acquire() switch
        {
            InstanceLockAcquireResult.Acquired => SingleInstanceOutcome.Primary,
            InstanceLockAcquireResult.Recovered => SingleInstanceOutcome.RecoveredPrimary,
            InstanceLockAcquireResult.AlreadyRunning => activationDispatcher.TryDispatch(activationRequest)
                ? SingleInstanceOutcome.ActivationRedirected
                : SingleInstanceOutcome.ActivationFailed,
            _ => throw new InvalidOperationException("The instance lock returned an unknown result.")
        };
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        instanceLock.Dispose();
    }
}

public enum SingleInstanceOutcome
{
    Primary,
    RecoveredPrimary,
    ActivationRedirected,
    ActivationFailed
}
