using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using Moq;
using SkiaSharp;
using TOTP.Core.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class NativeStartupSplashTests
{
    [AvaloniaTheory]
    [InlineData(360, 640, false, "en")]
    [InlineData(390, 840, false, "en")]
    [InlineData(430, 800, true, "de")]
    [InlineData(360, 640, true, "fr")]
    [InlineData(430, 800, false, "es")]
    public async Task EmptyAccountActions_RenderVisibleLocalizedText(int width, int height, bool dark, string culture)
    {
        var context = CreateContext(culture: culture);
        context.Snapshots.Setup(service => service.LoadAsync())
            .ReturnsAsync(Result.Ok(new AccountListSnapshot([], new HashSet<Guid>())));
        var view = CreateView(context.Model);
        view.UseNativeAccountList(new Border());
        var window = new Window { Width = width, Height = height, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Styles.Add(new StyleInclude(new Uri("avares://TOTP.Tests.Avalonia.Headless/"))
        {
            Source = new Uri("avares://TOTP.UI.Avalonia.Mobile/Styles/MobileStyles.axaml")
        });
        try
        {
            window.Show();
            window.SetRenderScaling(3.125);
            await context.Model.InitializeAsync();
            Assert.True(context.Model.HasNoAccounts);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var bitmap = window.CaptureRenderedFrame();
            Assert.NotNull(bitmap);
            using var encoded = new MemoryStream();
            bitmap.Save(encoded, new PngBitmapEncoderOptions());
            using var pixels = SKBitmap.Decode(encoded.ToArray());
            foreach (var (name, expected) in new[]
            {
                ("AddFirstAccountButton", context.Model.AddAccountText),
                ("ScanFirstAccountButton", context.Model.ScanQrText),
                ("ImportFirstAccountButton", context.Model.MigrationImport.Title)
            })
            {
                var button = Assert.Single(view.GetVisualDescendants().OfType<Button>(), control => control.Name == name);
                var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == expected);
                Assert.True(label.Bounds.Width > 0 && label.Bounds.Height >= label.TextLayout.Height - 1,
                    $"{name}: text bounds {label.Bounds}, text layout {label.TextLayout.Width}x{label.TextLayout.Height}.");
                var origin = label.TranslatePoint(default, button)!.Value;
                Assert.True(origin.Y >= 0 && origin.Y + label.Bounds.Height <= button.Bounds.Height + 1);
                AssertRenderedInk(label, window, pixels);
            }
            var help = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == context.Model.NoAccountsText);
            Assert.True(help.Bounds.Height >= help.TextLayout.Height - 1);
        }
        finally { window.Close(); }
    }

    private static void AssertRenderedInk(TextBlock label, Window window, SKBitmap pixels)
    {
        var color = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
        var origin = label.TranslatePoint(default, window)!.Value;
        var scale = window.RenderScaling;
        var ink = 0;
        for (var y = Math.Max(0, (int)(origin.Y * scale)); y < Math.Min(pixels.Height, (origin.Y + label.Bounds.Height) * scale); y++)
        for (var x = Math.Max(0, (int)(origin.X * scale)); x < Math.Min(pixels.Width, (origin.X + label.Bounds.Width) * scale); x++)
        {
            var pixel = pixels.GetPixel(x, y);
            if (Math.Abs(pixel.Red - color.R) + Math.Abs(pixel.Green - color.G) + Math.Abs(pixel.Blue - color.B) < 60)
                ink++;
        }
        Assert.True(ink > 10, $"No visible glyphs for '{label.Text}' in the composed frame.");
    }
}
