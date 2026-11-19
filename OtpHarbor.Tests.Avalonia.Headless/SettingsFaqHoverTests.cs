using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class SettingsFaqHoverTests
{
    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(800, 620)]
    public void HeaderHover_StaysNavyOnlyInDarkThemeAndPreservesChevronFeedback(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var reference = new Expander { Header = "Reference header", Width = 360 };
        var baseline = new Window { Content = reference, Width = 420, Height = 200 };
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            baseline.Show();
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(), control => control.Classes.Contains("settings-tabs"));
            tabs.SelectedIndex = 5;
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                baseline.RequestedThemeVariant = theme;
                window.UpdateLayout();
                baseline.UpdateLayout();
                baseline.MouseMove(new Point(-1, -1));
                var baselineHeader = Assert.Single(reference.GetVisualDescendants().OfType<ToggleButton>(), control => control.Name == "ExpanderHeader");
                Hover(baseline, baselineHeader);
                var baselineSurface = Surface(baselineHeader, "ToggleButtonBackground");
                var baselineChevron = Surface(baselineHeader, "ExpandCollapseChevronBorder");
                baseline.MouseMove(new Point(-1, -1));
                var headers = window.GetVisualDescendants().OfType<ToggleButton>()
                    .Where(control => control.Name == "ExpanderHeader" && control.IsEffectivelyVisible).ToArray();
                Assert.Equal(4, headers.Length);
                foreach (var header in headers)
                {
                    window.MouseMove(new Point(-1, -1));
                    window.UpdateLayout();
                    var beforeChevron = Surface(header, "ExpandCollapseChevronBorder");
                    Hover(window, header);
                    Assert.Equal(theme == ThemeVariant.Dark ? Color.Parse("#0E192D") : baselineSurface,
                        Surface(header, "ToggleButtonBackground"));
                    Assert.Equal(baselineChevron, Surface(header, "ExpandCollapseChevronBorder"));
                    Assert.NotEqual(beforeChevron, Surface(header, "ExpandCollapseChevronBorder"));
                }
            }
        }
        finally { baseline.Close(); window.Close(); }
    }

    private static Color Surface(ToggleButton header, string name) => Assert.IsAssignableFrom<ISolidColorBrush>(
        Assert.Single(header.GetVisualDescendants().OfType<Border>(), border => border.Name == name).Background).Color;

    private static void Hover(Window window, ToggleButton header)
    {
        header.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var chevron = Assert.Single(header.GetVisualDescendants().OfType<Border>(), border => border.Name == "ExpandCollapseChevronBorder");
        var point = chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.UpdateLayout();
        Assert.True(header.IsPointerOver);
    }
}
