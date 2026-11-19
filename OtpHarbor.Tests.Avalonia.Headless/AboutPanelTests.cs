using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Shared.About;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class AboutPanelTests
{
    [AvaloniaTheory]
    [InlineData(320, true, false, "en")]
    [InlineData(360, true, true, "de")]
    [InlineData(430, true, false, "fr")]
    [InlineData(320, false, false, "en")]
    [InlineData(360, false, true, "de")]
    [InlineData(430, false, false, "fr")]
    [InlineData(720, false, true, "es")]
    public void About_ReflowsLocalizedRowsWithLeftIconsAndPlatformSpecificSharing(int width, bool mobile, bool dark, string culture)
    {
        var strings = new AvaloniaStringCatalog();
        var model = new AboutViewModel(key => strings.Get(key, CultureInfo.GetCultureInfo(culture)), () => "3.2.65535.0",
            _ => Task.FromResult(true), mobile, (_, _) => Task.FromResult(true));
        var panel = new AboutPanel { DataContext = model };
        if (!mobile) panel.Classes.Add("compact");
        var scroll = new ScrollViewer { Content = panel, Margin = new Thickness(16), HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var window = new Window { Width = width, Height = 640, Content = scroll, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            var buttons = panel.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.Equal(mobile ? 4 : 3, buttons.Length);
            foreach (var button in buttons)
            {
                Assert.InRange(button.Bounds.Height, 44, 48);
                var icon = Assert.Single(button.GetVisualDescendants().OfType<SymbolIcon>());
                Assert.Equal(mobile ? 20 : 16, icon.IconSize);
                var link = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("about-link"));
                Assert.Equal(mobile ? 16 : 14, link.FontSize);
                Assert.True(icon.TranslatePoint(default, button)!.Value.X < 30);
                var texts = button.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text)).ToArray();
                foreach (var text in texts)
                {
                    var position = text.TranslatePoint(default, button)!.Value;
                    Assert.True(Math.Abs(position.Y + text.Bounds.Height / 2 - button.Bounds.Height / 2) <= 2);
                }
            }
            var logo = Assert.Single(panel.GetVisualDescendants().OfType<Image>());
            Assert.Equal(mobile ? 40 : 28, logo.Bounds.Width);
            Assert.Equal(mobile ? 40 : 28, logo.Bounds.Height);
            var title = Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("about-title"));
            Assert.Equal(mobile ? 24 : 18, title.FontSize);
            Assert.Contains(panel.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == "3.2.65535.0");
            var version = Assert.Single(panel.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == "3.2.65535.0");
            Assert.Equal(mobile ? 16 : 14, version.FontSize);
            var versionRow = Assert.IsType<Grid>(version.Parent);
            var versionLabel = Assert.Single(versionRow.Children.OfType<TextBlock>(), text => text is not SelectableTextBlock);
            Assert.True(Math.Abs(version.Bounds.Center.Y - versionLabel.Bounds.Center.Y) <= 2);
            Assert.Contains(panel.GetVisualDescendants().OfType<SymbolIcon>(), icon => icon.Kind == SymbolIconKind.License);
        }
        finally { window.Close(); }
    }
}
