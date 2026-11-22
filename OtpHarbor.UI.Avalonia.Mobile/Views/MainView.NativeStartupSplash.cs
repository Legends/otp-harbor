namespace TOTP.Avalonia.Mobile.Views;

public partial class MainView
{
    private bool _usesNativeStartupSplash;
    private bool _nativeStartupSplashRemoved;
    private bool _nativeStartupContentReported;

    private bool IsNativeStartupSplashPending => _usesNativeStartupSplash && !_nativeStartupSplashRemoved;

    public event EventHandler? NativeStartupContentReady;

    public void UseNativeStartupSplash()
    {
        _usesNativeStartupSplash = true;
        StartupLoadingOverlay.IsVisible = false;
        EvaluateStartupTransition();
    }

    public void NotifyNativeStartupSplashRemoved()
    {
        if (!_usesNativeStartupSplash || _nativeStartupSplashRemoved) return;
        _nativeStartupSplashRemoved = true;
        if (_startupTransitionCompleted) BeginSecondaryPreload();
    }

    private void ReportNativeStartupContentReady()
    {
        if (!IsNativeStartupSplashPending || _nativeStartupContentReported) return;
        _nativeStartupContentReported = true;
        NativeStartupContentReady?.Invoke(this, EventArgs.Empty);
    }
}
