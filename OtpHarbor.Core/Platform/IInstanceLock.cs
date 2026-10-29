namespace TOTP.Core.Platform;

public interface IInstanceLock : IDisposable
{
    InstanceLockAcquireResult Acquire();
}
