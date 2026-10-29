using TOTP.Core.Platform;

namespace TOTP.Infrastructure.Services;

public sealed class NamedMutexInstanceLock(string mutexName) : IInstanceLock
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private bool _acquired;

    public InstanceLockAcquireResult Acquire()
    {
        if (_acquired) throw new InvalidOperationException("The instance lock can only be acquired once.");
        _acquired = true;
        _mutex = new Mutex(false, mutexName, out var createdNew);
        try
        {
            if (!_mutex.WaitOne(0)) return InstanceLockAcquireResult.AlreadyRunning;
            _ownsMutex = true;
            return createdNew ? InstanceLockAcquireResult.Acquired : InstanceLockAcquireResult.Recovered;
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            return InstanceLockAcquireResult.Recovered;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null) return;
        try
        {
            if (_ownsMutex) mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
