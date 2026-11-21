using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 1)]
    [InlineData(520, 1.25)]
    [InlineData(520, 2.5)]
    [InlineData(1440, 1)]
    [InlineData(1440, 1.25)]
    [InlineData(1440, 2.5)]
    public void CardHeaderCorners_PreserveRoundedOutlineInAppearanceAndIconPacks(int width, double scaling)
    {
        var window = new SettingsWindow { Width = width, Height = 480 };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<ContentControl>("AppearanceSettingsHost")!.Content = settings;
            window.FindControl<ContentControl>("BrandIconSettingsHost")!.Content = settings;
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var tab in new[] { 0, 1 })
            {
                window.RequestedThemeVariant = theme;
                window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = tab;
                window.UpdateLayout();
                if (tab == 1)
                {
                    var installed = Named<Border>(window, "InstalledIconPackCard");
                    // Isolate the finished card from its entrance animation.
                    installed.IsVisible = true;
                }
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var panel = Named<StackPanel>(window, tab == 0 ? "AppearanceSettingsPanel" : "BrandIconsPanel");
                var cards = panel.Children.OfType<Border>().Where(card => card.IsVisible).ToArray();
                Assert.Equal(tab == 0 ? OperatingSystem.IsLinux() ? 4 : 3 : 4, cards.Length);
                foreach (var card in cards)
                {
                    var header = card.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("settings-card-header"));
                    using var cardImage = RenderCardSurface(card, scaling);
                    using var headerImage = RenderCardSurface(header, scaling);
                    foreach (var bitmap in new[] { cardImage, headerImage })
                    {
                        Assert.Equal(0, CardPixelAlpha(bitmap, 0, 0));
                        Assert.Equal(0, CardPixelAlpha(bitmap, bitmap.PixelSize.Width - 1, 0));
                        Assert.Equal(255, CardPixelAlpha(bitmap, bitmap.PixelSize.Width / 2, 1));
                    }
                    Assert.Equal(255, CardPixelAlpha(headerImage, 1, headerImage.PixelSize.Height - 2));
                    Assert.Equal(255, CardPixelAlpha(headerImage, headerImage.PixelSize.Width - 2, headerImage.PixelSize.Height - 2));
                }
            }
        }
        finally { window.Close(); }
    }

    private static RenderTargetBitmap RenderCardSurface(Control control, double scaling)
    {
        var bitmap = new RenderTargetBitmap(PixelSize.FromSize(control.Bounds.Size, scaling), new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(control);
        return bitmap;
    }

    private static byte CardPixelAlpha(Bitmap bitmap, int x, int y)
    {
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), pixel, 4, 4);
            return Marshal.ReadByte(pixel, 3);
        }
        finally { Marshal.FreeHGlobal(pixel); }
    }
}
