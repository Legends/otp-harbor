namespace TOTP.Core.Platform;

public interface IPlatformSessionEventSource
{
    bool IsSupported { get; }
    event EventHandler<PlatformSessionChangedEventArgs>? SessionChanged;
    void Start();
    void Stop();
}
