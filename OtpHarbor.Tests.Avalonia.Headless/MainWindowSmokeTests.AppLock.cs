using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Security;
using FluentResults;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(520, 620)]
    [InlineData(800, 620)]
    public async Task AppLockSettings_KeepLocalizedStateAndActionReachable(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var settings = new TestSettingsService();
        var strings = new AvaloniaStringCatalog();
        var localization = new AvaloniaLocalizationService(window.Resources, strings);
        using var model = new AuthorizationSettingsViewModel(new LayoutAuthorization(), new LayoutDialogs(),
            localization, new PasswordValidationService(), settingsService: settings, unattendedUnlock: new LayoutUnattended());
        try
        {
            window.Show();
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(), control => control.Classes.Contains("settings-tabs"));
            tabs.SelectedIndex = 2;
            var host = window.FindControl<ContentControl>("AuthorizationSettingsHost")!;
            host.Content = model;
            window.UpdateLayout();
            var panel = Assert.Single(host.GetVisualDescendants().OfType<Border>(), border => border.Name == "AppLockSettingsPanel");
            var button = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), value => value.Name == "AppLockToggleButton");
            Assert.Equal("AppLockToggleButton", button.Name);
            Assert.Contains("settings-card", panel.Classes);
            Assert.Same(model.ToggleAppLockCommand, button.Command);
            var scroller = window.FindControl<ScrollViewer>("SecuritySettingsScroll")!;
            foreach (var enabled in new[] { true, false })
            {
                settings.Current.AppLockEnabled = enabled;
                await model.RefreshAsync();
                foreach (var locale in new[] { "en", "de", "fr", "es" })
                {
                    localization.ApplyCulture(locale);
                    window.UpdateLayout();
                    var culture = CultureInfo.GetCultureInfo(locale);
                    Assert.Equal(strings.Get(enabled ? AvaloniaStringKeys.DisableAppLock : AvaloniaStringKeys.EnableAppLock, culture), AutomationProperties.GetName(button));
                    Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text ==
                        strings.Get(enabled ? AvaloniaStringKeys.AppLockEnabledDescription : AvaloniaStringKeys.AppLockDisabledDescription, culture));
                    Assert.All(host.GetVisualDescendants().OfType<RadioButton>(), control => Assert.Equal(enabled, control.IsEffectivelyEnabled));
                    Assert.True(button.IsEnabled);
                    Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                    button.BringIntoView();
                    window.UpdateLayout();
                    var bottom = button.TranslatePoint(new global::Avalonia.Point(0, button.Bounds.Height), scroller)!.Value.Y;
                    Assert.InRange(bottom, 1, scroller.Viewport.Height + 1);
                    foreach (var text in button.GetVisualDescendants().OfType<TextBlock>())
                        Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace <= text.Bounds.Width + 1);
                }
            }
        }
        finally { window.Close(); }
    }

    private sealed class LayoutAuthorization : IAuthorizationService
    {
        public AuthorizationResult RotationResult { get; set; } = AuthorizationResult.Success;
        public (string Current, string Next)? RotatedPasswords { get; private set; }
        public LayoutAuthorization() { State.SetConfiguration(true, PreferredUnlockMethod.Password); State.Unlock(); }
        public AuthorizationState State { get; } = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<bool> IsHelloAvailableAsync() => Task.FromResult(true);
        public Task<bool> IsUnlockMethodAvailableAsync(PreferredUnlockMethod method) => Task.FromResult(false);
        public Task<AuthorizationResult> TryUnlockOnStartupAsync() => throw new NotSupportedException();
        public Task<AuthorizationResult> TryUnlockOnStartupAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<AuthorizationResult> TryUnlockWithPasswordAsync(string password) => throw new NotSupportedException();
        public Task<AuthorizationResult> TryUnlockWithHelloAsync() => throw new NotSupportedException();
        public Task<AuthorizationResult> TryUnlockWithHelloAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<AuthorizationResult> ConfigurePasswordAsync(string password, string confirmation) => throw new NotSupportedException();
        public Task<AuthorizationResult> ConfigureHelloAsync() => throw new NotSupportedException();
        public Task<AuthorizationResult> ConfigureHelloAsync(string password) => throw new NotSupportedException();
        public Task<AuthorizationResult> ConfigureUnlockMethodAsync(PreferredUnlockMethod method, string password) => throw new NotSupportedException();
        public Task<AuthorizationResult> SetGateAsync(AuthorizationGateKind gate) => throw new NotSupportedException();
        public Task<AuthorizationResult> ChangePasswordAsync(string current, string next)
        {
            RotatedPasswords = (current, next);
            return Task.FromResult(RotationResult);
        }
        public Task<AuthorizationResult> SetAppLockEnabledAsync(bool enabled, string password) => throw new NotSupportedException();
        public void Logout() => State.Lock();
        public void Lock() => State.Lock();
    }

    private sealed class LayoutUnattended : IPlatformUnattendedUnlock
    {
        public string ProviderId => "layout-only";
        public Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(CancellationToken token = default) => Task.FromResult(PlatformQuickUnlockAvailability.Available);
        public Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(ReadOnlyMemory<byte> key, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(PlatformQuickUnlockWrapperV2 wrapper, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Result> RemoveAsync(PlatformQuickUnlockWrapperV2 wrapper, CancellationToken token = default) => throw new NotSupportedException();
    }

    private sealed class LayoutDialogs : IAvaloniaDialogService
    {
        public Task<bool> ConfirmImportFileAsync(AuthenticatorImportViewModel model, AccountImportReadSummary summary, string fileName, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountImportResolution?> ReviewImportAsync(ImportReviewViewModel review, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(ConfirmationDialogRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public Task<string?> PromptForPasswordAsync(PasswordDialogRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ChoiceDialogResult> ChooseAsync(ChoiceDialogRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public Task ShowMessageAsync(MessageDialogRequest request, CancellationToken token = default) => throw new NotSupportedException();
    }
}
