using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
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

    internal event Action<int, Result, Intent?>? ActivityResultReceived;

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
        if (!IsChangingConfigurations && Application is OtpHarborApplication host)
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
    }

    internal void DetachScreenCapturePolicy()
    {
        if (_screenCapturePolicy is not null)
            _screenCapturePolicy.PropertyChanged -= ScreenCapturePolicyChanged;
        _screenCapturePolicy = null;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        ActivityResultReceived?.Invoke(requestCode, resultCode, data);
    }

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
}
