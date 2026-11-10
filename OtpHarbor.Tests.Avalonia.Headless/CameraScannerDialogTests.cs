using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Localization;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class CameraScannerDialogTests
{
    [AvaloniaFact]
    public void WideCameraPreview_MeetsTheHeaderWithoutAGrayLetterboxStripe()
    {
        var window = new CameraScannerDialogWindow { RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            window.Show();
            var preview = window.FindControl<Image>("CameraPreviewImage")!;
            preview.Source = new DrawingImage
            {
                Drawing = new GeometryDrawing
                {
                    Brush = Brushes.CornflowerBlue,
                    Geometry = new RectangleGeometry(new Rect(0, 0, 160, 20))
                }
            };
            preview.IsVisible = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var header = window.FindControl<Grid>("ScannerHeader")!;
            var viewport = window.FindControl<Grid>("CameraViewport")!;
            var headerBottom = header.TranslatePoint(new Point(0, header.Bounds.Height), window)!.Value.Y;
            Assert.Equal(headerBottom, viewport.TranslatePoint(default, window)!.Value.Y, precision: 2);
            Assert.Same(viewport, preview.Parent);
            Assert.Equal(Stretch.Uniform, preview.Stretch);

            using var bitmap = new RenderTargetBitmap(new PixelSize(560, 420), new Vector(96, 96));
            bitmap.Render(window);
            var point = viewport.TranslatePoint(new Point(viewport.Bounds.Width / 2, 3), window)!.Value;
            var pixel = Marshal.AllocHGlobal(4);
            try
            {
                bitmap.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 4);
                var channels = new byte[4];
                Marshal.Copy(pixel, channels, 0, channels.Length);
                Assert.Equal(new byte[] { 0, 0, 0, 255 }, channels);
            }
            finally
            {
                Marshal.FreeHGlobal(pixel);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WarningIcon_HasATransparentInteriorWithoutADarkOuterShell()
    {
        var window = new CameraScannerDialogWindow();
        try
        {
            window.Show();
            var triangle = window.FindControl<AvaloniaPath>("ScannerWarningTriangle")!;
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(triangle.Fill).Color);
            Assert.Equal(Color.Parse("#F9D43B"), Assert.IsAssignableFrom<ISolidColorBrush>(triangle.Stroke).Color);
            var artwork = Assert.IsType<Canvas>(triangle.Parent);
            Assert.Equal(2, artwork.Children.OfType<AvaloniaPath>().Count());
            var mark = Assert.Single(artwork.Children.OfType<AvaloniaPath>(), path => path != triangle);
            Assert.Equal(Color.Parse("#F9D43B"), Assert.IsAssignableFrom<ISolidColorBrush>(mark.Fill).Color);
            Assert.Null(mark.Stroke);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScannerChrome_FollowsTheAppPaletteAndUpdatesWhenTheThemeChanges()
    {
        var application = Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        var window = new CameraScannerDialogWindow();
        try
        {
            application.RequestedThemeVariant = ThemeVariant.Light;
            window.Show();
            Color? lightHeader = null;
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
            {
                application.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(theme, window.ActualThemeVariant);

                var surface = window.FindControl<Border>("ScannerSurface")!;
                var header = window.FindControl<Grid>("ScannerHeader")!;
                var footer = window.FindControl<Border>("ScannerFooter")!;
                AssertPalette("BrushWindowBackground", surface.Background);
                AssertPalette("BrushBorder", surface.BorderBrush);
                AssertPalette("BrushTitleBarBackground", header.Background);
                AssertPalette("BrushSurface", footer.Background);
                AssertPalette("BrushBorder", footer.BorderBrush);
                AssertPalette("BrushTextPrimary", window.FindControl<TextBlock>("ScannerStatusText")!.Foreground);
                AssertPalette("BrushTextSecondary", window.FindControl<TextBlock>("ScannerMessageText")!.Foreground);
                AssertPalette("BrushAccent", window.FindControl<CameraScanOverlay>("ScanTargetOverlay")!.FrameBrush);
                foreach (var name in new[] { "ScannerHelpButton", "CloseScannerButton", "RetryScannerButton", "OpenQrImageButton" })
                {
                    var button = window.FindControl<Button>(name)!;
                    AssertPalette("BrushTextPrimary", button.Foreground);
                    if (!button.Classes.Contains("scanner-header-control"))
                        AssertPalette("BrushSurfaceAlternate", button.Background);
                }

                var headerColor = Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color;
                if (theme == ThemeVariant.Light)
                {
                    Assert.True(headerColor.R > 220 && headerColor.G > 220 && headerColor.B > 220);
                    lightHeader = headerColor;
                }
                else
                {
                    Assert.NotEqual(lightHeader, headerColor);
                }
            }
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = previousTheme;
        }

        void AssertPalette(string key, IBrush? actual)
        {
            Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var expected));
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
        }
    }

    [AvaloniaFact]
    public void Instructions_AreHiddenInitiallyAndUseTheCompleteActiveLocaleInTooltip()
    {
        var window = new CameraScannerDialogWindow();
        var catalog = new AvaloniaStringCatalog();
        var localization = new AvaloniaLocalizationService(window.Resources, catalog);
        try
        {
            window.Show();
            var help = window.FindControl<Button>("ScannerHelpButton")!;
            var scroller = Assert.IsType<ScrollViewer>(ToolTip.GetTip(help));
            var text = Assert.IsType<TextBlock>(scroller.Content);
            Assert.False(ToolTip.GetIsOpen(help));
            Assert.DoesNotContain(text, window.GetVisualDescendants());

            foreach (var language in new[] { "en", "de", "fr", "es" })
            {
                localization.ApplyCulture(language);
                var culture = CultureInfo.GetCultureInfo(language);
                Assert.Equal(catalog.Get(AvaloniaStringKeys.QrScannerSupportedFormats, culture), text.Text);
                Assert.Equal(text.Text, AutomationProperties.GetHelpText(help));
                Assert.Equal(catalog.Get(AvaloniaStringKeys.QrScannerHelp, culture), AutomationProperties.GetName(help));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Instructions_OpenOnKeyboardFocusAndCloseWhenFocusLeaves()
    {
        var window = new CameraScannerDialogWindow();
        try
        {
            window.Show();
            var help = window.FindControl<Button>("ScannerHelpButton")!;
            Assert.True(help.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTip.GetIsOpen(help));

            var content = Assert.IsType<ScrollViewer>(ToolTip.GetTip(help));
            var tooltip = Assert.Single(content.GetVisualAncestors().OfType<ToolTip>());
            Assert.Equal(new CornerRadius(5), tooltip.CornerRadius);

            var surface = window.FindControl<Border>("ScannerSurface")!;
            surface.Focusable = true;
            Assert.True(surface.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolTip.GetIsOpen(help));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Instructions_OpenOnHover()
    {
        var window = new CameraScannerDialogWindow();
        try
        {
            window.Show();
            var help = window.FindControl<Button>("ScannerHelpButton")!;
            ToolTip.SetShowDelay(help, 0);
            var center = help.TranslatePoint(new Point(16, 16), window)!.Value;
            window.MouseMove(center);
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTip.GetIsOpen(help));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseButton_ClosesWithPointerOrEscape(bool useEscape)
    {
        var window = new CameraScannerDialogWindow();
        var close = window.FindControl<Button>("CloseScannerButton")!;
        close.Command = new ActionCommand(window.Close);
        try
        {
            window.Show();
            if (useEscape)
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            else
            {
                var center = close.TranslatePoint(new Point(16, 16), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
            }

            Assert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Surface_ClipsAllFourCornersAndUpdatesAfterResize()
    {
        var window = new CameraScannerDialogWindow();
        try
        {
            window.Show();
            var surface = window.FindControl<Border>("ScannerSurface")!;
            Assert.Equal(new CornerRadius(5), window.CornerRadius);
            Assert.Equal(new CornerRadius(5), surface.CornerRadius);
            Assert.Equal(new CornerRadius(5), window.FindControl<Border>("ScannerFooter")!.CornerRadius);
            foreach (var size in new[] { new Size(560, 420), new Size(420, 320), new Size(820, 480) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var clip = Assert.IsType<RectangleGeometry>(surface.Clip);
                Assert.Equal(size, surface.Bounds.Size);
                Assert.Equal(surface.Bounds, clip.Rect);
                Assert.Equal(surface.CornerRadius.TopLeft, clip.RadiusX);
                Assert.Equal(clip.RadiusX, clip.RadiusY);
                foreach (var point in new[]
                {
                    new Point(1, 1), new Point(size.Width - 1, 1),
                    new Point(1, size.Height - 1), new Point(size.Width - 1, size.Height - 1)
                })
                    Assert.False(clip.FillContains(point), $"Corner {point} is included in {clip.Rect} with radius {clip.RadiusX}/{clip.RadiusY}");
                Assert.True(clip.FillContains(new Point(size.Width / 2, size.Height / 2)));
            }

            Assert.Equal(new Thickness(0), window.BorderThickness);
            Assert.Equal(Win32Properties.WindowCornerPreference.DoNotRound, Win32Properties.GetWindowCornerPreference(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(420, 320)]
    [InlineData(560, 420)]
    [InlineData(820, 480)]
    public void OverlayAndActions_FitCompactAndRegularWindowsInEveryLocale(int width, int height)
    {
        var window = new CameraScannerDialogWindow { Width = width, Height = height };
        var catalog = new AvaloniaStringCatalog();
        var localization = new AvaloniaLocalizationService(window.Resources, catalog);
        try
        {
            window.Show();
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
            window.FindControl<Image>("CameraPreviewImage")!.IsVisible = true;
            Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
            Assert.Empty(window.GetVisualDescendants().OfType<ProductTitleBar>());
            var close = window.FindControl<Button>("CloseScannerButton")!;
            Assert.True(close.IsCancel);

            foreach (var language in new[] { "en", "de", "fr", "es" })
            {
                localization.ApplyCulture(language);
                window.FindControl<TextBlock>("ScannerStatusText")!.Text =
                    localization.GetString(AvaloniaStringKeys.CameraSearching);
                window.FindControl<TextBlock>("ScannerMessageText")!.Text =
                    localization.GetString(AvaloniaStringKeys.CameraNotFound) + " " +
                    localization.GetString(AvaloniaStringKeys.CameraReconnectHint);
                window.UpdateLayout();

                foreach (var name in new[] { "ScannerHelpButton", "CloseScannerButton", "RetryScannerButton", "OpenQrImageButton" })
                {
                    var button = window.FindControl<Button>(name)!;
                    var isHeaderControl = name is "ScannerHelpButton" or "CloseScannerButton";
                    Assert.True(button.Bounds.Height >= (isHeaderControl ? 32 : 44), $"{language}/{width}: {name} is too small");
                    Assert.Equal(new CornerRadius(5), button.CornerRadius);
                    if (isHeaderControl)
                    {
                        Assert.Equal(new Thickness(0), button.BorderThickness);
                        Assert.Equal(new Size(32, 32), button.Bounds.Size);
                    }
                    var origin = button.TranslatePoint(default, window)!.Value;
                    Assert.True(origin.X >= 0 && origin.Y >= 0);
                    Assert.True(origin.X + button.Bounds.Width <= window.ClientSize.Width);
                    Assert.True(origin.Y + button.Bounds.Height <= window.ClientSize.Height);
                }

                var overlay = window.FindControl<CameraScanOverlay>("ScanTargetOverlay")!;
                Assert.True(overlay.Bounds.Height >= 40, $"{language}/{width}: no usable scan area");
                Assert.False(overlay.IsHitTestVisible);
                var preview = window.FindControl<Image>("CameraPreviewImage")!;
                Assert.Equal(Stretch.Uniform, preview.Stretch);
                Assert.True(Assert.IsType<Grid>(preview.Parent).Bounds.Width >= width - 2);
                var closeOrigin = close.TranslatePoint(default, window)!.Value;
                Assert.True(closeOrigin.X > width / 2d && closeOrigin.Y < 20);
                var header = window.FindControl<Grid>("ScannerHeader")!;
                foreach (var button in new[] { window.FindControl<Button>("ScannerHelpButton")!, close })
                {
                    var top = button.TranslatePoint(default, header)!.Value.Y;
                    var bottom = header.Bounds.Height - top - button.Bounds.Height;
                    Assert.True(top >= 8 && bottom >= 8);
                    Assert.Equal(top, bottom, precision: 2);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class ActionCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
