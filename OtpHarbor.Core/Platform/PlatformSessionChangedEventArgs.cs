namespace TOTP.Core.Platform;

public sealed class PlatformSessionChangedEventArgs(PlatformSessionState state) : EventArgs
{
    public PlatformSessionState State { get; } = state;
}
