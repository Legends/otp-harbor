using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TOTP.Avalonia.Desktop;

[assembly: AvaloniaTestApplication(typeof(TOTP.Tests.Avalonia.Headless.HeadlessTestApplication))]

namespace TOTP.Tests.Avalonia.Headless;

public static class HeadlessTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
