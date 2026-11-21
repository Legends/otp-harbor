using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class SettingsCategoryHeaderTests
{
    [AvaloniaTheory]
    [InlineData("en", 520)]
    [InlineData("de", 520)]
    [InlineData("fr", 520)]
    [InlineData("es", 520)]
    [InlineData("en", 720)]
    [InlineData("de", 720)]
    [InlineData("fr", 720)]
    [InlineData("es", 720)]
    public void CategoryTitles_FitTheirTabsWithoutClippingInBothThemes(string language, int width)
    {
        var window = new SettingsWindow { MinWidth = 520, Width = width, Height = 620 };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        localization.ApplyCulture(language);
        try
        {
            window.Show();
            var navigation = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(), control => control.Classes.Contains("settings-tabs"));
            var tabs = navigation.Items.Cast<TabItem>().ToArray();
            Assert.Equal(7, tabs.Length);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var selected in new[] { 0, 4 })
            {
                window.RequestedThemeVariant = theme;
                navigation.SelectedIndex = selected;
                window.UpdateLayout();
                Assert.Equal(localization.GetString(AvaloniaStringKeys.Miscellaneous),
                    Assert.Single(((Panel)tabs[4].Header!).Children.OfType<TextBlock>()).Text);
                foreach (var tab in tabs)
                {
                    var label = Assert.Single(((Panel)tab.Header!).Children.OfType<TextBlock>());
                    var position = label.TranslatePoint(default, tab)!.Value;
                    Assert.True(position.X >= -0.5, $"{language}: {label.Text} starts outside the tab.");
                    Assert.True(position.X + label.Bounds.Width <= tab.Bounds.Width + 0.5,
                        $"{language}: {label.Text} extends outside the tab ({position.X + label.Bounds.Width} > {tab.Bounds.Width}).");
                    Assert.True(position.Y >= -0.5 && position.Y + label.Bounds.Height <= tab.Bounds.Height + 0.5);
                    Assert.True(label.TextLayout.Height <= label.Bounds.Height + 0.5);
                    Assert.True(label.TextLayout.Width <= label.Bounds.Width + 0.5);
                    Assert.Equal(TextTrimming.None, label.TextTrimming);
                    Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                    Assert.NotEmpty(label.TextLayout.TextLines);
                }
            }
        }
        finally { window.Close(); }
    }
}
