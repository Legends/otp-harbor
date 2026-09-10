using Avalonia.Threading;
using TOTP.Core.Security.Interfaces;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaHelloPromptWindowHandleProvider(
    AvaloniaWindowCoordinator windowCoordinator,
    IAvaloniaLocalizationService localization) : IHelloPromptWindowHandleProvider
{
    private readonly AvaloniaWindowCoordinator _windowCoordinator =
        windowCoordinator ?? throw new ArgumentNullException(nameof(windowCoordinator));

    public string VerificationMessage => localization.GetString(AvaloniaStringKeys.UnlockWithQuickUnlock);

    public IDisposable BeginPrompt()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(BeginPrompt).GetAwaiter().GetResult();

        return new PromptRegistration(_windowCoordinator.BeginNativePrompt());
    }

    private sealed class PromptRegistration(IDisposable registration) : IDisposable
    {
        public void Dispose()
        {
            if (Dispatcher.UIThread.CheckAccess()) registration.Dispose();
            else Dispatcher.UIThread.InvokeAsync(registration.Dispose).GetAwaiter().GetResult();
        }
    }

    public nint GetActiveWindowHandle()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread
                .InvokeAsync(GetActiveWindowHandle)
                .GetAwaiter()
                .GetResult();
        }

        return _windowCoordinator.CurrentActivationTarget?
            .TryGetPlatformHandle()?
            .Handle ?? nint.Zero;
    }
}
