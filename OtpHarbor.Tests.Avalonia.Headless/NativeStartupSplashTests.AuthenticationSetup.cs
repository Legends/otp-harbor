using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using TOTP.Avalonia.Mobile.Views;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class NativeStartupSplashTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task AppLockPasswordConfirmation_UsesShortLocalizedActionWithoutChangingMainAction(string culture)
    {
        var context = CreateContext(appLock: true, culture: culture);
        await context.Model.InitializeAsync();
        Assert.True(context.Model.IsUnlockVisible);

        context.Authorization.Setup(service => service.TryUnlockWithPasswordAsync("Synthetic password"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        context.Model.UnlockPassword = "Synthetic password";
        await context.Model.UnlockAsync();
        Assert.True(context.Model.IsAccountsVisible);

        await context.Model.ShowSettingsAsync();
        context.Model.ShowSecuritySettingsCommand.Execute(null);
        Assert.True(context.Model.IsSecuritySettingsVisible);
        await context.Model.BeginDisableAppLockAsync();
        Assert.True(context.Model.IsDisableAppLockConfirmationVisible);
        context.Model.AppLockRecoveryPassword = "Synthetic confirmation";
        var view = CreateView(context.Model);
        var window = new Window { Width = 360, Height = 640, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var confirm = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => button.Command == context.Model.ConfirmDisableAppLockCommand);
            var main = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => button.Command == context.Model.ToggleAppLockCommand);
            var shortLabel = Assert.Single(confirm.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == context.Model.DeactivateAppLockConfirmationText);
            Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == context.Model.AppLockActionText);
            Assert.NotEqual(context.Model.AppLockActionText, shortLabel.Text);
            Assert.True(shortLabel.TextLayout.Width <= shortLabel.Bounds.Width + 1, $"Confirmation text is clipped in {culture}.");
            Assert.True(confirm.Bounds.Height >= 44);
            Assert.Equal(VerticalAlignment.Center, confirm.VerticalContentAlignment);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(360, 640, false, "en")]
    [InlineData(360, 640, true, "en")]
    [InlineData(430, 800, true, "en")]
    [InlineData(720, 900, false, "de")]
    [InlineData(360, 480, true, "fr")]
    [InlineData(430, 640, false, "es")]
    public async Task InitialAuthenticationScreen_ReflowsAndKeepsEveryChoiceReachable(int width, int height, bool dark, string culture)
    {
        var context = await PrepareInitialAuthenticationAsync(culture: culture);
        var view = CreateView(context.Model);
        var window = new Window { Width = width, Height = height, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var setup = Assert.Single(view.GetVisualDescendants().OfType<InitialAuthenticationSetupView>());
            var scroller = setup.FindControl<ScrollViewer>("AuthenticationSetupScroll")!;
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
            var buttons = new[] { "InitialBiometricChoice", "InitialDeviceLockChoice", "InitialMasterPasswordChoice", "InitialNoAppLockChoice" }
                .Select(name => setup.FindControl<Button>(name)!).ToArray();
            Assert.All(buttons, button => Assert.True(button.IsVisible));
            Assert.Equal(context.Model.MasterPasswordText, Assert.Single(buttons[2].GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("auth-title")).Text);
            Assert.Contains(buttons[2].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == context.Model.InitialAuthenticationDefault);
            if (width == 430 && height == 800) SaveAuthenticationPreview(window, "dark-top");
            foreach (var button in buttons)
            {
                button.BringIntoView();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var location = button.TranslatePoint(default, window)!.Value;
                Assert.True(location.X >= 0 && location.X + button.Bounds.Width <= width + 1);
                Assert.True(location.Y >= 0 && location.Y + button.Bounds.Height <= height + 1);
                Assert.True(button.Bounds.Height >= 48);
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            }
            var settingsHint = setup.FindControl<TextBlock>("InitialAuthenticationSettingsHint")!;
            settingsHint.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var hintLocation = settingsHint.TranslatePoint(default, window)!.Value;
            Assert.True(hintLocation.Y >= 0 && hintLocation.Y + settingsHint.Bounds.Height <= height + 1);
            if (width == 430 && height == 800) SaveAuthenticationPreview(window, "dark-bottom");
            var password = buttons[2];
            password.BringIntoView();
            window.UpdateLayout();
            var content = Assert.IsAssignableFrom<Control>(password.Content);
            var before = content.TranslatePoint(default, password)!.Value;
            window.MouseMove(password.TranslatePoint(new Point(25, 25), window)!.Value);
            password.Focus(NavigationMethod.Tab);
            window.UpdateLayout();
            Assert.Equal(before, content.TranslatePoint(default, password)!.Value);
            password.IsEnabled = false;
            window.UpdateLayout();
            Assert.Equal(before, content.TranslatePoint(default, password)!.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InitialAuthenticationScreen_AlwaysOffersPasswordAndNoLock(bool biometric, bool credential)
    {
        var context = await PrepareInitialAuthenticationAsync(biometric, credential);
        var view = CreateView(context.Model);
        var window = new Window { Width = 360, Height = 640, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            var setup = Assert.Single(view.GetVisualDescendants().OfType<InitialAuthenticationSetupView>());
            Assert.Equal(biometric, setup.FindControl<Button>("InitialBiometricChoice")!.IsVisible);
            Assert.Equal(credential, setup.FindControl<Button>("InitialDeviceLockChoice")!.IsVisible);
            Assert.True(setup.FindControl<Button>("InitialMasterPasswordChoice")!.IsVisible);
            Assert.True(setup.FindControl<Button>("InitialNoAppLockChoice")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(InitialAppLockChoice.MasterPassword, "InitialMasterPasswordChoice")]
    [InlineData(InitialAppLockChoice.StrongBiometrics, "InitialBiometricChoice")]
    [InlineData(InitialAppLockChoice.DeviceScreenLock, "InitialDeviceLockChoice")]
    [InlineData(InitialAppLockChoice.AppLockDisabled, "InitialNoAppLockChoice")]
    public async Task InitialAuthenticationScreen_PointerSelectionExecutesTheRequestedChoice(InitialAppLockChoice choice, string name)
    {
        var context = await PrepareInitialAuthenticationAsync();
        context.Authorization.Setup(value => value.CompleteInitialAppLockSetupAsync(
                InitialAppLockChoice.AppLockDisabled, It.IsAny<CancellationToken>()))
            .Callback(() => context.Preferences.AppLockEnabled = false).ReturnsAsync(AuthorizationResult.Success);
        var view = CreateView(context.Model);
        var window = new Window { Width = 360, Height = 640, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            var setup = Assert.Single(view.GetVisualDescendants().OfType<InitialAuthenticationSetupView>());
            var button = setup.FindControl<Button>(name)!;
            button.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(context.Model.IsInitialQuickUnlockOfferVisible);
            Assert.Equal(choice != InitialAppLockChoice.AppLockDisabled, context.Model.IsAppLockEnabled);
            if (choice == InitialAppLockChoice.AppLockDisabled)
                context.Authorization.Verify(value => value.CompleteInitialAppLockSetupAsync(choice, It.IsAny<CancellationToken>()), Times.Once);
            else
            {
                PreferredUnlockMethod? method = choice switch
                {
                    InitialAppLockChoice.StrongBiometrics => PreferredUnlockMethod.PlatformQuickUnlock,
                    InitialAppLockChoice.DeviceScreenLock => PreferredUnlockMethod.PlatformDeviceCredential,
                    _ => null
                };
                context.Authorization.Verify(value => value.CompleteInitialQuickUnlockSetupAsync(method, It.IsAny<CancellationToken>()), Times.Once);
            }
        }
        finally { window.Close(); }
    }

    private async Task<Context> PrepareInitialAuthenticationAsync(bool biometric = true, bool credential = true, string culture = "en")
    {
        var context = CreateContext(configured: false, appLock: true, culture: culture);
        context.Authorization.Setup(value => value.IsHelloAvailableAsync()).ReturnsAsync(biometric);
        context.Authorization.Setup(value => value.IsUnlockMethodAvailableAsync(PreferredUnlockMethod.PlatformDeviceCredential)).ReturnsAsync(credential);
        context.Authorization.Setup(value => value.ConfigurePasswordAsync("synthetic setup", "synthetic setup"))
            .Callback(() => { context.State.SetConfiguration(true, PreferredUnlockMethod.Password); context.State.Unlock(); })
            .ReturnsAsync(AuthorizationResult.Success);
        await context.Model.InitializeAsync();
        await context.Model.FinishOnboardingAsync();
        context.Model.SetupPassword = "synthetic setup";
        context.Model.SetupConfirmation = "synthetic setup";
        await context.Model.ConfigureAsync();
        Assert.True(context.Model.IsInitialQuickUnlockOfferVisible);
        return context;
    }

    private static void SaveAuthenticationPreview(Window window, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "authentication-setup-previews");
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
