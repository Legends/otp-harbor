using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using TOTP.Avalonia.Mobile;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Mobile.Views;

namespace TOTP.Avalonia.Android;

[Application(AllowBackup = false, UsesCleartextTraffic = false)]
public class OtpHarborApplication : AvaloniaAndroidApplication<MobileApp>
{
    private ServiceProvider? _services;

    protected OtpHarborApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).AfterSetup(_ =>
        {
            _services = AndroidCompositionRoot.Build(this);
            var viewModel = _services.GetRequiredService<MobileShellViewModel>();
            var app = global::Avalonia.Application.Current as MobileApp
                ?? throw new InvalidOperationException("The mobile Avalonia application is unavailable.");
            app.MainViewFactory = () =>
            {
                var view = new MainView { DataContext = viewModel };
                viewModel.InitializeCommand.Execute(null);
                return view;
            };
        });
    }

    public void NotifyEnteredBackground(bool lockImmediately)
    {
        _services?.GetService<IMobileLifecycleSink>()?.OnEnteredBackground(lockImmediately);
    }

    public void AttachActivity(MainActivity activity)
    {
        _services?.GetService<AndroidActivityProvider>()?.Attach(activity);
        var viewModel = _services?.GetService<MobileShellViewModel>();
        if (viewModel is not null)
            activity.AttachScreenCapturePolicy(viewModel);
    }

    public void DetachActivity(MainActivity activity)
    {
        activity.DetachScreenCapturePolicy();
        _services?.GetService<AndroidActivityProvider>()?.Detach(activity);
    }

    public void NotifyReturnedToForeground()
    {
        _services?.GetService<IMobileLifecycleSink>()?.OnReturnedToForeground();
    }

    public override void OnTerminate()
    {
        _services?.Dispose();
        _services = null;
        base.OnTerminate();
    }
}
