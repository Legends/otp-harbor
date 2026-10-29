namespace TOTP.Core.Platform;

public interface IActivationDispatcher
{
    bool TryDispatch(ApplicationActivationRequest request);
}
