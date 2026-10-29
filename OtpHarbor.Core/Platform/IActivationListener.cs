namespace TOTP.Core.Platform;

public interface IActivationListener : IDisposable
{
    void Start(Action<ApplicationActivationRequest> onActivation);
}
