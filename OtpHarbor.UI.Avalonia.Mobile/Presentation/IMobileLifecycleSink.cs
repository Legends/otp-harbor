namespace TOTP.Avalonia.Mobile.Presentation;

public interface IMobileLifecycleSink
{
    void OnEnteredBackground(bool lockImmediately);
    void OnReturnedToForeground();
}
