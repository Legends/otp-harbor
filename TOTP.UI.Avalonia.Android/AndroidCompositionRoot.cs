using Android.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Extensions;
using TOTP.Infrastructure.Services;
using TOTP.Platform.Android;

namespace TOTP.Avalonia.Android;

internal static class AndroidCompositionRoot
{
    public static ServiceProvider Build(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = new ServiceCollection();
        var paths = new AndroidApplicationPaths(context);
        var fileSecurity = new AndroidFileSecurity(context);
        var configuration = new ConfigurationBuilder().Build();

        services.AddSingleton(context);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPlatformApplicationPaths>(paths);
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddProvider(new AndroidQuickUnlockLogProvider()));
        services.AddSingleton<AndroidActivityProvider>();
        services.AddSingleton<IAndroidBiometricPrompt, AndroidBiometricPrompt>();
        services.AddSingleton<IMobileQrScanner, AndroidQrScanner>();
        services.AddSingleton<IMobileQrImageFactory, MobileQrImageFactory>();
        services.AddSingleton<IMobileDocumentService, AndroidDocumentService>();
        services.AddSingleton<AndroidPlatformUnattendedUnlock>();
        services.AddSingleton<IPlatformUnattendedUnlock>(provider =>
            provider.GetRequiredService<AndroidPlatformUnattendedUnlock>());
        services.AddSingleton<IPlatformQuickUnlock>(provider =>
            provider.GetRequiredService<AndroidPlatformUnattendedUnlock>());
        // Keep the interactive adapter last: single-service consumers are the
        // biometric enrollment/settings flow, while sessions enumerate both.
        services.AddSingleton<IPlatformQuickUnlock, AndroidPlatformQuickUnlock>();
        services.AddInfrastructure(configuration, paths, fileSecurity);
        services.AddSingleton<IAsyncPlatformClipboard, AndroidPlatformClipboard>();
        services.AddSingleton<AsyncClipboardService>();
        services.AddSingleton<IAsyncClipboardService>(provider =>
            provider.GetRequiredService<AsyncClipboardService>());
        services.AddSingleton<MobileStringCatalog>();
        services.AddSingleton<MobileShellViewModel>();
        services.AddSingleton<IMobileLifecycleSink>(provider =>
            provider.GetRequiredService<MobileShellViewModel>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
