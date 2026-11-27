using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using TOTP.Avalonia.Desktop;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class SettingsWindowChromeTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(2.5)]
    public void RoundedFrame_HasOneOutlineAndTransparentCornersAfterResize(double scaling)
    {
        var window = new SettingsWindow();
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            var frame = window.FindControl<Border>("SettingsWindowFrame")!;
            var surface = window.FindControl<Grid>("SettingsWindowSurface")!;
            Assert.Equal(Win32Properties.WindowCornerPreference.DoNotRound, Win32Properties.GetWindowCornerPreference(window));
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(new Thickness(0), window.BorderThickness);
            Assert.True(window.CanResize);
            Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color.A);
            Assert.Contains(window.KeyBindings, binding => binding.Gesture?.Key == Key.Escape);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(900, 720), new Size(520, 480), new Size(1440, 820) })
            {
                window.RequestedThemeVariant = theme;
                window.Width = size.Width;
                window.Height = size.Height;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(new CornerRadius(18), window.CornerRadius);
                Assert.Equal(window.CornerRadius, frame.CornerRadius);
                Assert.Equal(size, frame.Bounds.Size);
                Assert.Null(frame.Clip);
                var clip = Assert.IsType<RectangleGeometry>(surface.Clip);
                Assert.Equal(new Rect(surface.Bounds.Size), clip.Rect);
                Assert.Equal(window.CornerRadius.TopLeft - frame.BorderThickness.Left, clip.RadiusX);
                Assert.Equal(clip.RadiusX, clip.RadiusY);
                foreach (var point in new[] { new Point(1, 1), new Point(clip.Rect.Width - 1, 1),
                             new Point(1, clip.Rect.Height - 1), new Point(clip.Rect.Width - 1, clip.Rect.Height - 1) })
                    Assert.False(clip.FillContains(point));
                var pixels = PixelSize.FromSize(size, scaling);
                using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
                bitmap.Render(window);
                foreach (var point in new[] { new PixelPoint(1, 1), new PixelPoint(pixels.Width - 2, 1),
                             new PixelPoint(1, pixels.Height - 2), new PixelPoint(pixels.Width - 2, pixels.Height - 2) })
                    Assert.Equal(0, Alpha(bitmap, point));
                Assert.Equal(255, Alpha(bitmap, new PixelPoint(pixels.Width / 2, pixels.Height / 2)));
                AssertCornerOutline(frame, bitmap, scaling);
            }
            window.CornerRadius = new CornerRadius(12);
            window.UpdateLayout();
            Assert.Equal(window.CornerRadius, frame.CornerRadius);
            Assert.Equal(11, Assert.IsType<RectangleGeometry>(surface.Clip).RadiusX);
            frame.BorderThickness = new Thickness(2);
            window.UpdateLayout();
            Assert.Equal(10, Assert.IsType<RectangleGeometry>(surface.Clip).RadiusX);
            Assert.Equal(new Rect(surface.Bounds.Size), Assert.IsType<RectangleGeometry>(surface.Clip).Rect);
        }
        finally { window.Close(); }
    }

    private static void AssertCornerOutline(Border frame, Bitmap bitmap, double scaling)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, new PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(encoded.ToArray());
        var outline = Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color;
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color;
        var contrast = Math.Abs(background.R - outline.R) + Math.Abs(background.G - outline.G) + Math.Abs(background.B - outline.B);
        var radius = frame.CornerRadius.TopLeft;
        var offset = (int)Math.Floor((radius - (radius - 0.5) / Math.Sqrt(2)) * scaling);
        foreach (var point in new[] { new PixelPoint(offset, offset), new PixelPoint(bitmap.PixelSize.Width - 1 - offset, offset),
                     new PixelPoint(offset, bitmap.PixelSize.Height - 1 - offset), new PixelPoint(bitmap.PixelSize.Width - 1 - offset, bitmap.PixelSize.Height - 1 - offset) })
        {
            var closest = Enumerable.Range(-1, 3).SelectMany(y => Enumerable.Range(-1, 3).Select(x =>
                    pixels.GetPixel(point.X + x, point.Y + y)))
                .Where(color => color.Alpha > 128)
                .Select(color => Math.Abs(color.Red - outline.R) + Math.Abs(color.Green - outline.G) + Math.Abs(color.Blue - outline.B))
                .DefaultIfEmpty(int.MaxValue).Min();
            Assert.True(closest < contrast * 0.75,
                $"Rounded outline at {point} was covered (scale {scaling}, color difference {closest}).");
        }
    }

    private static byte Alpha(Bitmap bitmap, PixelPoint point)
    {
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(point.X, point.Y, 1, 1), pixel, 4, 4);
            return Marshal.ReadByte(pixel, 3);
        }
        finally { Marshal.FreeHGlobal(pixel); }
    }
}
