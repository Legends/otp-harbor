using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class NativePromptFocusTests
{
    [AvaloniaFact]
    public void NativePrompt_DropsTopmostAndDefersActivationUntilCompletion()
    {
        var coordinator = new AvaloniaWindowCoordinator();
        var main = new Window();
        var settings = new Window { Topmost = true };
        coordinator.RegisterMainWindow(main);
        main.Show();
        settings.Show(main);
        using var dialog = coordinator.RegisterOwnedDialog(settings);
        try
        {
            var prompt = coordinator.BeginNativePrompt();
            Assert.False(settings.Topmost);
            settings.WindowState = WindowState.Minimized;
            coordinator.ActivateCurrent();
            Assert.Equal(WindowState.Minimized, settings.WindowState);

            prompt.Dispose();
            prompt.Dispose();

            Assert.True(settings.Topmost);
            Assert.Equal(WindowState.Normal, settings.WindowState);
        }
        finally { settings.Close(); main.Close(); }
    }

    [AvaloniaFact]
    public void NativePrompt_ClosedOwnerIsNotReopened()
    {
        var coordinator = new AvaloniaWindowCoordinator();
        var main = new Window();
        coordinator.RegisterMainWindow(main);
        main.Show();
        using var prompt = coordinator.BeginNativePrompt();
        main.Close();
        prompt.Dispose();
        Assert.False(main.IsVisible);
    }

    [AvaloniaFact]
    public void NativePrompt_RequiresVisibleOwner()
    {
        var coordinator = new AvaloniaWindowCoordinator();
        Assert.Throws<InvalidOperationException>(() => coordinator.BeginNativePrompt());
        coordinator.RegisterMainWindow(new Window());
        Assert.Throws<InvalidOperationException>(() => coordinator.BeginNativePrompt());
    }

    [AvaloniaTheory]
    [InlineData("en", "Unlock with quick unlock")]
    [InlineData("de", "Mit Schnellentsperrung entsperren")]
    [InlineData("fr", "Déverrouiller avec le déverrouillage rapide")]
    [InlineData("es", "Desbloquear con desbloqueo rápido")]
    public void NativePrompt_MessageComesFromActiveLocale(string culture, string expected)
    {
        var localization = new AvaloniaLocalizationService(new ResourceDictionary(), new AvaloniaStringCatalog());
        localization.ApplyCulture(culture);
        var provider = new AvaloniaHelloPromptWindowHandleProvider(new AvaloniaWindowCoordinator(), localization);
        Assert.Equal(expected, provider.VerificationMessage);
    }
}
