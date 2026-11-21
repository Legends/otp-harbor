using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 416)]
    [InlineData(900, 720)]
    [InlineData(1440, 820)]
    public async Task FaqCards_KeepSevenLocalizedTopicsReachableWithoutSearchOrFilters(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(height, 480) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 5;
            window.UpdateLayout();
            var page = window.FindControl<StackPanel>("FaqSettingsPanel")!;
            var scroll = window.FindControl<ScrollViewer>("FaqSettingsScroll")!;
            var cards = page.Children.OfType<Border>().ToArray();
            var sections = cards.Select(card => Assert.IsType<Expander>(card.Child)).ToArray();
            Assert.Equal(7, cards.Length);
            Assert.Equal("FaqMasterPasswordRecoveryExpander", Assert.Single(sections, section => section.IsExpanded).Name);
            Assert.DoesNotContain(page.GetVisualDescendants(), value => value is TextBox or ComboBox or RadioButton or TabControl);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<SymbolIcon>(), value => value.Kind == SymbolIconKind.Search);
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            var keys = new[] { AvaloniaStringKeys.FaqMasterPasswordRecoveryQuestion, AvaloniaStringKeys.FaqOlderVaultsQuestion,
                AvaloniaStringKeys.FaqImportIconPacksQuestion, AvaloniaStringKeys.FaqImportFormatsQuestion,
                AvaloniaStringKeys.FaqCreateBackupQuestion, AvaloniaStringKeys.FaqBackupLocationsQuestion, AvaloniaStringKeys.FaqForgottenPasswordQuestion };
            foreach (var locale in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(locale);
                window.RequestedThemeVariant = theme;
                window.UpdateLayout();
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.FaqDescription));
                for (var index = 0; index < sections.Length; index++)
                {
                    var section = sections[index];
                    section.IsExpanded = true;
                    section.BringIntoView();
                    window.UpdateLayout();
                    await WaitUntilAsync(() => sections.Count(value => value.IsExpanded) == 1);
                    Assert.Same(section, Assert.Single(sections, value => value.IsExpanded));
                    Assert.Contains("expanded", cards[index].Classes);
                    Assert.True(window.TryFindResource("BrushAccent", theme, out var accent));
                    Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(accent).Color, Assert.IsAssignableFrom<ISolidColorBrush>(cards[index].BorderBrush).Color);
                    var title = Assert.Single(Assert.IsType<Grid>(section.Header).Children.OfType<TextBlock>());
                    Assert.Equal(localization.GetString(keys[index]), title.Text);
                    Assert.True(title.TextLayout.Width <= title.Bounds.Width + 1);
                    Assert.True(title.Bounds.Width >= 80);
                    var badge = Assert.Single(Assert.IsType<Grid>(section.Header).Children.OfType<Border>());
                    Assert.Equal(40, badge.Bounds.Width);
                    Assert.Equal(40, badge.Bounds.Height);
                    var header = section.GetVisualDescendants().OfType<ToggleButton>().Single(value => value.Name == "ExpanderHeader" && ReferenceEquals(value.TemplatedParent, section));
                    Assert.True(header.Bounds.Height >= 56);
                    Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Stretch, header.HorizontalContentAlignment);
                    var answer = Assert.IsType<Border>(section.Content);
                    var texts = answer.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
                    Assert.NotEmpty(texts);
                    foreach (var text in texts)
                    {
                        var left = text.TranslatePoint(default, page)!.Value.X;
                        Assert.InRange(left, 0, page.Bounds.Width);
                        Assert.InRange(left + text.Bounds.Width, 0, page.Bounds.Width + 1);
                        Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1);
                    }
                    if (section.Name == "FaqCreateBackupExpander")
                        Assert.Equal(localization.GetString(AvaloniaStringKeys.FaqCreateBackupAnswer), Assert.Single(texts).Text);
                    if (section.Name == "FaqIconPacksExpander")
                    {
                        var guide = Named<StackPanel>(section, "IconPackFaqContent");
                        var download = Named<HyperlinkButton>(section, "IconPackBuilderDownloadLink");
                        Assert.Equal(guide.Bounds.Width <= 700 ? 1 : 0, Grid.GetRow(download));
                        foreach (var state in new[] { "normal", "hover", "focus", "disabled" })
                        {
                            window.MouseMove(new Point(-1, -1));
                            if (state == "hover") MovePointerTo(window, download);
                            if (state == "focus") download.Focus(NavigationMethod.Tab);
                            download.IsEnabled = state != "disabled";
                            window.UpdateLayout();
                            Assert.True(download.Bounds.Height >= 44);
                            Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, download.VerticalContentAlignment);
                            var label = Assert.IsType<SymbolIconLabel>(download.Content);
                            var center = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), download)!.Value.Y;
                            Assert.InRange(Math.Abs(center - download.Bounds.Height / 2), 0, 1);
                        }
                        download.IsEnabled = true;
                    }
                    var bottom = answer.TranslatePoint(new Point(0, answer.Bounds.Height), scroll)!.Value.Y;
                    scroll.Offset = new Vector(0, scroll.Offset.Y + bottom - scroll.Viewport.Height);
                    window.UpdateLayout();
                    Assert.InRange(answer.TranslatePoint(new Point(0, answer.Bounds.Height), scroll)!.Value.Y, 0, scroll.Viewport.Height + 1);
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                }
                var first = sections[0];
                first.IsExpanded = true;
                await WaitUntilAsync(() => sections.Count(value => value.IsExpanded) == 1);
                var firstHeader = Named<ToggleButton>(first, "ExpanderHeader");
                firstHeader.BringIntoView();
                window.UpdateLayout();
                firstHeader.Focus(NavigationMethod.Tab);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.False(first.IsExpanded);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.True(first.IsExpanded);
            }
        }
        finally { window.Close(); }
    }
}
