using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData("quick", 360, 540)]
    [InlineData("credential", 360, 540)]
    [InlineData("both", 360, 540)]
    [InlineData("quick", 720, 400)]
    [InlineData("credential", 720, 400)]
    [InlineData("both", 720, 400)]
    public void FirstSetupQuickUnlockOffer_KeepsLocalizedActionsWithinViewport(string methods, int width, int height)
    {
        var catalog = new AvaloniaStringCatalog();
        foreach (var locale in new[] { "en", "de", "fr", "es" })
        {
            var culture = CultureInfo.GetCultureInfo(locale);
            string Text(string key) => catalog.Get(key, culture);
            var title = Text(AvaloniaStringKeys.InitialQuickUnlockTitle);
            var message = Text(AvaloniaStringKeys.InitialQuickUnlockDescription);
            Window window = methods == "both"
                ? new ChoiceDialogWindow { DataContext = new ChoiceDialogViewModel(new ChoiceDialogRequest(
                    title, message, NotificationSeverity.Information, Text(AvaloniaStringKeys.EnableQuickUnlock),
                    Text(AvaloniaStringKeys.DeviceScreenLock), Text(AvaloniaStringKeys.NotNow), StackActions: true)) }
                : new ConfirmationDialogWindow { DataContext = new ConfirmationDialogViewModel(new ConfirmationDialogRequest(
                    title, message, NotificationSeverity.Information,
                    Text(methods == "quick" ? AvaloniaStringKeys.EnableQuickUnlock : AvaloniaStringKeys.DeviceScreenLock),
                    Text(AvaloniaStringKeys.NotNow), StackActions: true)) };
            window.Width = Math.Min(520, width - 32);
            window.MaxWidth = width - 32;
            window.MaxHeight = height - 32;
            window.FindControl<ScrollViewer>("DialogContentScroll")!.MaxHeight = Math.Max(120, height - 200);
            var card = Assert.Single(window.GetLogicalDescendants().OfType<Border>(),
                value => value.Classes.Contains("dialog-message-card"));
            Assert.IsType<TextBlock>(card.Child).Width = window.Width - 100;
            try
            {
                window.Show();
                window.UpdateLayout();
                var buttons = window.GetVisualDescendants().OfType<Button>().Where(value => value.IsEffectivelyVisible).ToArray();
                Assert.Equal(methods == "both" ? 3 : 2, buttons.Length);
                foreach (var button in buttons)
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    var topLeft = button.TranslatePoint(default, window);
                    var bottomRight = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), window);
                    Assert.NotNull(topLeft);
                    Assert.NotNull(bottomRight);
                    Assert.True(topLeft.Value.X >= -0.5 && topLeft.Value.Y >= -0.5
                        && bottomRight.Value.X <= window.Bounds.Width + 0.5 && bottomRight.Value.Y <= window.Bounds.Height + 0.5,
                        $"The {locale} {methods} action exceeds the {window.Bounds.Size} viewport.");
                    foreach (var label in button.GetVisualDescendants().OfType<TextBlock>())
                        Assert.True(label.TextLayout.WidthIncludingTrailingWhitespace <= label.Bounds.Width + 1,
                            $"The {locale} {methods} action label is clipped.");
                }
            }
            finally { window.Close(); }
        }
    }
}
