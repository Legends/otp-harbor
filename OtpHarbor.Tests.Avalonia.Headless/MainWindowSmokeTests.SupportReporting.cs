using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(520, 620)]
    [InlineData(800, 620)]
    public void SupportReporting_HasSeparateLocalizedBorderAndReachableBoundAction(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var catalog = new AvaloniaStringCatalog();
        var localization = new AvaloniaLocalizationService(window.Resources, catalog);
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.Show();
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(),
                control => control.Classes.Contains("settings-tabs"));
            tabs.SelectedIndex = 4;
            window.FindControl<ContentControl>("MiscSettingsHost")!.Content = settings;
            window.UpdateLayout();
            var logging = Assert.Single(window.GetVisualDescendants().OfType<Border>(),
                border => border.Name == "MiscSettingsPanel");
            var report = Assert.Single(window.GetVisualDescendants().OfType<Border>(),
                border => border.Name == "ProblemReportSettingsPanel");
            Assert.Same(logging.Parent, report.Parent);
            Assert.Contains("panel", report.Classes);
            Assert.Equal(logging.BorderThickness, report.BorderThickness);
            Assert.Equal(logging.BorderBrush, report.BorderBrush);
            Assert.Equal(logging.Background, report.Background);
            Assert.Equal(logging.Padding, report.Padding);
            Assert.DoesNotContain(logging.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "ReportOnGitHubButton");
            var button = Assert.Single(report.GetVisualDescendants().OfType<Button>());
            Assert.Equal("ReportOnGitHubButton", button.Name);
            Assert.Same(settings.ReportOnGitHubCommand, button.Command);
            var label = Assert.IsType<SymbolIconLabel>(button.Content);
            var heading = Assert.Single(report.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("section-title"));
            var disclosure = Assert.Single(report.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("secondary"));
            var scroller = window.FindControl<ScrollViewer>("SupportSettingsScroll")!;
            foreach (var locale in new[] { "en", "de", "fr", "es" })
            {
                localization.ApplyCulture(locale);
                window.UpdateLayout();
                var culture = CultureInfo.GetCultureInfo(locale);
                Assert.Equal(catalog.Get(AvaloniaStringKeys.ReportProblemTitle, culture), heading.Text);
                Assert.Equal(catalog.Get(AvaloniaStringKeys.GitHubReportNotice, culture), disclosure.Text);
                Assert.Equal(catalog.Get(AvaloniaStringKeys.ReportOnGitHub, culture), label.Text);
                Assert.True(report.Bounds.Top > logging.Bounds.Bottom);
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                Assert.True(disclosure.Bounds.Width <= report.Bounds.Width);
                button.BringIntoView();
                window.UpdateLayout();
                var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), scroller)!.Value.Y;
                Assert.InRange(bottom, 1, scroller.Viewport.Height + 1);
            }
        }
        finally { window.Close(); }
    }
}
