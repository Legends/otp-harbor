using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
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
                var clip = Assert.IsType<RectangleGeometry>(frame.Clip);
                Assert.Equal(frame.Bounds, clip.Rect);
                Assert.Equal(window.CornerRadius.TopLeft, clip.RadiusX);
                Assert.Equal(clip.RadiusX, clip.RadiusY);
                foreach (var point in new[] { new Point(1, 1), new Point(size.Width - 1, 1),
                             new Point(1, size.Height - 1), new Point(size.Width - 1, size.Height - 1) })
                    Assert.False(clip.FillContains(point));
                var pixels = PixelSize.FromSize(size, scaling);
                using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
                bitmap.Render(frame);
                foreach (var point in new[] { new PixelPoint(1, 1), new PixelPoint(pixels.Width - 2, 1),
                             new PixelPoint(1, pixels.Height - 2), new PixelPoint(pixels.Width - 2, pixels.Height - 2) })
                    Assert.Equal(0, Alpha(bitmap, point));
                Assert.Equal(255, Alpha(bitmap, new PixelPoint(pixels.Width / 2, pixels.Height / 2)));
            }
            window.CornerRadius = new CornerRadius(12);
            window.UpdateLayout();
            Assert.Equal(window.CornerRadius, frame.CornerRadius);
            Assert.Equal(12, Assert.IsType<RectangleGeometry>(frame.Clip).RadiusX);
        }
        finally { window.Close(); }
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
