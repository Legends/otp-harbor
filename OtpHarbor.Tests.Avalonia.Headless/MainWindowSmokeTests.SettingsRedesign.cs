using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Import;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(960, 820)]
    public async Task IconPackCards_ShowInstalledStatusWithFadeAndReclaimSpaceOnRemoval(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 1;
            window.FindControl<ContentControl>("BrandIconSettingsHost")!.Content = settings;
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                window.RequestedThemeVariant = theme;
                window.UpdateLayout();
                var installed = Named<Border>(window, "InstalledIconPackCard");
                var manage = Named<Border>(window, "ManageIconPackCard");
                Assert.False(installed.IsVisible);
                var withoutInstalled = manage.Bounds.Top;
                var opacitySamples = new List<double>();
                void RecordOpacity(object? sender, AvaloniaPropertyChangedEventArgs args)
                {
                    if (args.Property == Visual.OpacityProperty) opacitySamples.Add(installed.Opacity);
                }
                installed.PropertyChanged += RecordOpacity;
                installed.Classes.Add("installed");
                window.UpdateLayout();
                window.CaptureRenderedFrame()?.Dispose();
                Assert.True(installed.IsVisible);
                Assert.True(installed.Bounds.Height > 0);
                Assert.True(manage.Bounds.Top > withoutInstalled);
                await WaitUntilAsync(() => installed.Opacity >= .999);
                installed.PropertyChanged -= RecordOpacity;
                Assert.Contains(opacitySamples, opacity => opacity < 1);
                var about = Named<Border>(window, "IconPackAboutCard");
                var link = about.GetVisualDescendants().OfType<HyperlinkButton>().Single();
                Assert.Equal(new Uri("https://github.com/Legends/otp-harbor-icon-pack-builder"), link.NavigateUri);
                var rights = about.GetVisualDescendants().OfType<Expander>().Single();
                rights.IsExpanded = true;
                window.UpdateLayout();
                Assert.Contains(rights.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.BrandIconsHelp));
                Assert.Contains(rights.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.IconPackResponsibility));
                rights.IsExpanded = false;
                installed.Classes.Remove("installed");
                window.UpdateLayout();
                Assert.False(installed.IsVisible);
                Assert.Equal(withoutInstalled, manage.Bounds.Top, 1);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(960, 820)]
    public void SettingsSidebarAndAppearance_ReflowAndRetainKeyboardBindings(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            var tabs = window.FindControl<TabControl>("SettingsCategories")!;
            window.FindControl<ContentControl>("AppearanceSettingsHost")!.Content = settings;
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                window.RequestedThemeVariant = theme;
                tabs.SelectedIndex = 0;
                window.UpdateLayout();
                var items = tabs.GetVisualDescendants().OfType<TabItem>().ToArray();
                Assert.Equal(7, items.Length);
                Assert.All(items, item => Assert.True(item.Bounds.Height >= 44));
                Assert.All(items.Zip(items.Skip(1)), pair => Assert.True(pair.First.Bounds.Bottom <= pair.Second.Bounds.Top));
                var rail = Named<Border>(tabs, "SettingsNavigationRail");
                var panel = Named<StackPanel>(window, "AppearanceSettingsPanel");
                Assert.True(panel.TranslatePoint(default, window)!.Value.X >= rail.TranslatePoint(default, window)!.Value.X + rail.Bounds.Width);
                AssertSurface(Named<Border>(items[0], "NavigationPill"), "BrushAccent", window, theme);
                Assert.Equal(localization.GetString(AvaloniaStringKeys.AppearanceDescription),
                    panel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == localization.GetString(AvaloniaStringKeys.AppearanceDescription)).Text);
                var languagePicker = Named<ComboBox>(panel, "AppearanceLanguagePicker");
                var themePicker = Named<ComboBox>(panel, "AppearanceThemePicker");
                var scale = Named<NumericUpDown>(panel, "AppearanceQrScalePicker");
                Assert.Equal(settings.SelectedLanguage, languagePicker.SelectedItem);
                Assert.Equal(settings.SelectedTheme, themePicker.SelectedItem);
                Assert.Equal(1, scale.Minimum);
                Assert.Equal(6, scale.Maximum);
                Assert.Equal(.5m, scale.Increment);
                scale.BringIntoView();
                window.MouseMove(new Point(-1, -1));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var valueInput = Named<TextBox>(scale, "PART_TextBox");
                var valueText = Named<TextPresenter>(valueInput, "PART_TextPresenter");
                var valueSurface = Named<Border>(valueInput, "PART_BorderElement");
                Assert.Equal(HorizontalAlignment.Center, valueInput.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, valueInput.VerticalContentAlignment);
                Assert.Equal(TextAlignment.Center, valueText.TextAlignment);
                Assert.Equal(19, valueText.FontSize);
                var valueCenter = valueText.TranslatePoint(new Point(valueText.Bounds.Width / 2, valueText.Bounds.Height / 2), scale)!.Value;
                Assert.InRange(Math.Abs(valueCenter.X - scale.Bounds.Width / 2), 0, 1);
                Assert.InRange(Math.Abs(valueCenter.Y - scale.Bounds.Height / 2), 0, 1);
                var normalBackground = Assert.IsAssignableFrom<ISolidColorBrush>(valueSurface.Background).Color;
                var normalBorder = Assert.IsAssignableFrom<ISolidColorBrush>(valueSurface.BorderBrush).Color;
                var normalForeground = Assert.IsAssignableFrom<ISolidColorBrush>(valueInput.Foreground).Color;
                window.MouseMove(valueInput.TranslatePoint(new Point(valueInput.Bounds.Width / 2, valueInput.Bounds.Height / 2), window)!.Value);
                Dispatcher.UIThread.RunJobs();
                Assert.True(valueInput.IsPointerOver);
                Assert.Equal(normalBackground, Assert.IsAssignableFrom<ISolidColorBrush>(valueSurface.Background).Color);
                Assert.Equal(normalBorder, Assert.IsAssignableFrom<ISolidColorBrush>(valueSurface.BorderBrush).Color);
                Assert.Equal(normalForeground, Assert.IsAssignableFrom<ISolidColorBrush>(valueInput.Foreground).Color);
                Assert.True(valueInput.Focus());
                window.UpdateLayout();
                Assert.True(valueInput.IsFocused);
                Assert.NotEqual(0, Assert.IsAssignableFrom<ISolidColorBrush>(valueSurface.BorderBrush).Color.A);
                var decrease = Named<RepeatButton>(scale, "PART_DecreaseButton");
                var increase = Named<RepeatButton>(scale, "PART_IncreaseButton");
                Assert.True(decrease.TranslatePoint(default, scale)!.Value.X < increase.TranslatePoint(default, scale)!.Value.X);
                scale.Value = 2;
                ClickControl(window, increase);
                Assert.Equal(2.5m, settings.QrPreviewScaleFactor);
                ClickControl(window, decrease);
                Assert.Equal(2, settings.QrPreviewScaleFactor);
                Assert.Equal(OperatingSystem.IsLinux(), Named<Border>(panel, "AppearanceInterfaceScaleCard").IsVisible);
                Assert.Equal(panel.Bounds.Width <= 520 ? 1 : 0, Grid.GetRow(languagePicker));
                foreach (var control in new Control[] { languagePicker, themePicker, scale, Named<ToggleSwitch>(panel, "AppearanceNextTokenSwitch"), Named<ToggleSwitch>(panel, "AppearanceIssuerLogoSwitch") })
                {
                    control.BringIntoView();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var left = control.TranslatePoint(default, panel)!.Value.X;
                    Assert.True(control.Bounds.Width > 0);
                    Assert.InRange(left, 0, panel.Bounds.Width);
                    Assert.InRange(left + control.Bounds.Width, 0, panel.Bounds.Width + 1);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                }
                var scroller = Named<ScrollViewer>(window, "AppearanceSettingsScroll");
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                var toggle = Named<ToggleSwitch>(panel, "AppearanceNextTokenSwitch");
                toggle.BringIntoView();
                window.UpdateLayout();
                toggle.Focus();
                var before = settings.ShowNextToken;
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.Equal(!before, settings.ShowNextToken);
                items[0].Focus();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.Equal(1, tabs.SelectedIndex);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                Assert.Equal(0, tabs.SelectedIndex);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(960, 820)]
    public async Task DiagnosticDisclosure_UsesFullWidthAndSemanticSurfaces(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var diagnostics = new DiagnosticsViewModel(new SummaryDiagnostics(), localization,
            environmentProvider: new SummaryEnvironment(), logSwitch: new SummaryLogSwitch());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 4;
            window.FindControl<ContentControl>("SupportDiagnosticsHost")!.Content = diagnostics;
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                window.RequestedThemeVariant = theme;
                await diagnostics.RefreshAsync();
                window.UpdateLayout();
                var expander = Named<Expander>(window, "DiagnosticsDetailsExpander");
                foreach (var expanded in new[] { false, true })
                {
                    expander.IsExpanded = expanded;
                    window.MouseMove(new Point(-1, -1));
                    window.UpdateLayout();
                    var parent = Assert.IsAssignableFrom<Control>(expander.Parent);
                    Assert.Equal(parent.Bounds.Width, expander.Bounds.Width, 1);
                    var header = Named<ToggleButton>(expander, "ExpanderHeader");
                    Assert.Equal(expander.Bounds.Width, header.Bounds.Width, 1);
                    AssertSurface(Named<Border>(header, "ToggleButtonBackground"), "BrushSurfaceAlternate", window, theme);
                    MovePointerTo(window, header);
                    AssertSurface(Named<Border>(header, "ToggleButtonBackground"), "BrushSurfaceHover", window, theme);
                    if (expanded)
                    {
                        AssertSurface(Named<Border>(expander, "ExpanderContent"), "BrushSurfaceAlternate", window, theme);
                        var report = Named<TextBox>(expander, "DiagnosticsDetailsText");
                        Assert.Equal(diagnostics.SupportInformation, report.Text);
                        Assert.Equal(TextWrapping.Wrap, report.TextWrapping);
                        Assert.True(report.IsReadOnly);
                        var presenter = expander.GetVisualDescendants().OfType<ContentPresenter>().Single(value => value.Name == "PART_ContentPresenter" && ReferenceEquals(value.TemplatedParent, expander));
                        Assert.Equal(presenter.Bounds.Width, report.Bounds.Width, 1);
                        AssertSurface(Named<Border>(report, "PART_BorderElement"), "BrushSurfaceAlternate", window, theme);
                    }
                    header.Focus();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    Assert.Equal(!expanded, expander.IsExpanded);
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(960, 820)]
    public void AuthenticatorDropdown_ThemesPopupAndPreservesSourceGuidance(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 3;
            var host = window.FindControl<ContentControl>("ImportExportSettingsHost")!;
            var content = (Control)host.ContentTemplate!.Build(null)!;
            host.ContentTemplate = null;
            host.Content = content;
            window.UpdateLayout();
            var import = content.GetVisualDescendants().OfType<SettingsImportPanel>().Single();
            var model = new AuthenticatorImportViewModel(localization.GetString, new NoAction(), new NoAction());
            import.DataContext = model;
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                model.RefreshLanguage();
                window.RequestedThemeVariant = theme;
                window.UpdateLayout();
                var choice = Named<ComboBox>(import, "ExportHelpSource");
                window.MouseMove(new Point(-1, -1));
                AssertSurface(Named<Border>(choice, "Background"), "BrushSurfaceAlternate", window, theme);
                MovePointerTo(window, choice);
                AssertSurface(Named<Border>(choice, "Background"), "BrushSurfaceHover", window, theme);
                choice.Focus();
                window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(choice.IsDropDownOpen);
                var popup = Named<Popup>(choice, "PART_Popup");
                var surface = Assert.IsType<Border>(popup.Child);
                AssertSurface(surface, "BrushSurface", window, theme);
                Assert.True(surface.Bounds.Width >= choice.Bounds.Width - 1);
                var item = surface.GetVisualDescendants().OfType<ComboBoxItem>().First();
                item.IsSelected = true;
                item.Focus();
                window.UpdateLayout();
                AssertSurface(Named<ContentPresenter>(item, "PART_ContentPresenter"), "BrushAccountItemSelectedBackground", window, theme);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Assert.False(choice.IsDropDownOpen);
                Assert.Same(model.Sources[1], model.SelectedSource);
                Assert.True(model.HasGuidance);
                Assert.False(string.IsNullOrWhiteSpace(model.Instructions));
            }
        }
        finally { window.Close(); }
    }

    private static T Named<T>(Visual parent, string name) where T : Control =>
        parent.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void AssertSurface(Control control, string resource, Window window, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(resource, theme, out var expected));
        var actual = control switch
        {
            Border border => border.Background,
            ContentPresenter presenter => presenter.Background,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color, Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
    }

    private static void MovePointerTo(Window window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value);
        window.UpdateLayout();
        Assert.True(control.IsPointerOver);
    }

    private static void ClickControl(Window window, Control control)
    {
        MovePointerTo(window, control);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
