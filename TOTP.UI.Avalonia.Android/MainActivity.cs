using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Window;
using Avalonia.Android;
using System.ComponentModel;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

[Activity(
    Label = "OTP Harbor",
    Theme = "@style/OtpHarborTheme",
    Icon = "@drawable/app_icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity
{
    private MobileShellViewModel? _screenCapturePolicy;
    private bool _backNavigationInProgress;
    private BackInvokedCallback? _backInvokedCallback;
    private bool _isBackInvokedCallbackRegistered;

    internal event Action<int, Result, Intent?>? ActivityResultReceived;
    internal bool IsInternalQrScannerActive { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Application is OtpHarborApplication host) host.AttachActivity(this);
    }

    protected override void OnStart()
    {
        base.OnStart();
        if (Application is OtpHarborApplication host)
            host.NotifyReturnedToForeground();
    }

    protected override void OnStop()
    {
        if (!IsChangingConfigurations &&
            !IsInternalQrScannerActive &&
            Application is OtpHarborApplication host)
            host.NotifyEnteredBackground(IsDeviceUnavailable());
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        if (Application is OtpHarborApplication host) host.DetachActivity(this);
        base.OnDestroy();
    }

    internal void AttachScreenCapturePolicy(MobileShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (ReferenceEquals(_screenCapturePolicy, viewModel))
        {
            ApplyScreenCapturePolicy();
            return;
        }

        DetachScreenCapturePolicy();
        _screenCapturePolicy = viewModel;
        _screenCapturePolicy.PropertyChanged += ScreenCapturePolicyChanged;
        ApplyScreenCapturePolicy();
        UpdateSystemBackCallback();
    }

    internal void DetachScreenCapturePolicy()
    {
        UnregisterSystemBackCallback();
        if (_screenCapturePolicy is not null)
            _screenCapturePolicy.PropertyChanged -= ScreenCapturePolicyChanged;
        _screenCapturePolicy = null;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        ActivityResultReceived?.Invoke(requestCode, resultCode, data);
    }

#pragma warning disable CA1422 // Android's compatibility callback also receives gesture navigation.
    public override void OnBackPressed() => _ = HandleBackNavigationAsync();

    private async Task HandleBackNavigationAsync()
    {
        if (_backNavigationInProgress) return;
        _backNavigationInProgress = true;
        try
        {
            if (Application is OtpHarborApplication host
                && await host.TryHandleBackNavigationAsync())
            {
                UpdateSystemBackCallback();
                return;
            }

            UnregisterSystemBackCallback();
            base.OnBackPressed();
        }
        finally
        {
            _backNavigationInProgress = false;
        }
    }
#pragma warning restore CA1422

    private bool IsDeviceUnavailable()
    {
        var keyguard = GetSystemService(Context.KeyguardService) as KeyguardManager;
        var power = GetSystemService(Context.PowerService) as PowerManager;
        return keyguard?.IsDeviceLocked == true || power?.IsInteractive == false;
    }

    private void ScreenCapturePolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MobileShellViewModel.IsScreenCaptureProtectionRequired))
            ApplyScreenCapturePolicy();
        if (args.PropertyName == nameof(MobileShellViewModel.CanHandleSystemBack))
            UpdateSystemBackCallback();
    }

    private void UpdateSystemBackCallback()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;

        if (_screenCapturePolicy?.CanHandleSystemBack == true)
        {
            if (_isBackInvokedCallbackRegistered) return;
            _backInvokedCallback ??= new BackInvokedCallback(
                () => _ = HandleBackNavigationAsync());
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                IOnBackInvokedDispatcher.PriorityDefault,
                _backInvokedCallback);
            _isBackInvokedCallbackRegistered = true;
            return;
        }

        UnregisterSystemBackCallback();
    }

    private void UnregisterSystemBackCallback()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)
            || !_isBackInvokedCallbackRegistered
            || _backInvokedCallback is null)
        {
            return;
        }

        OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backInvokedCallback);
        _isBackInvokedCallbackRegistered = false;
    }

    private void ApplyScreenCapturePolicy()
    {
        var window = Window;
        if (window is null) return;

#if OTP_HARBOR_MARKETING_CAPTURE
        window.ClearFlags(WindowManagerFlags.Secure);
#else
        if (_screenCapturePolicy?.IsScreenCaptureProtectionRequired == true)
            window.AddFlags(WindowManagerFlags.Secure);
        else
            window.ClearFlags(WindowManagerFlags.Secure);
#endif
    }

    private sealed class BackInvokedCallback(Action invoke)
        : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked() => invoke();
    }
}
