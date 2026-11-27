using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
    [InlineData(520)]
    [InlineData(900)]
    public void FirstSettingsTabs_MatchExistingPageAndSectionHeadingSizes(int width)
    {
        var window = new SettingsWindow { Width = width, Height = 720 };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var model = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.Show();
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.FindControl<ContentControl>("AppearanceSettingsHost")!.Content = model;
            window.FindControl<ContentControl>("BrandIconSettingsHost")!.Content = model;
            var tabs = window.FindControl<TabControl>("SettingsCategories")!;
            foreach (var culture in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(culture);
                window.RequestedThemeVariant = theme;
                tabs.SelectedIndex = 2;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var security = Named<StackPanel>(window, "SecuritySettingsPanel");
                var referenceTitle = security.GetVisualDescendants().OfType<TextBlock>().Single(text =>
                    text.Text == localization.GetString(AvaloniaStringKeys.Security));
                var securityHost = window.FindControl<ContentControl>("AuthorizationSettingsHost")!;
                if (securityHost.Content is null)
                {
                    var content = securityHost.ContentTemplate!.Build(null);
                    securityHost.ContentTemplate = null;
                    securityHost.Content = content;
                    window.UpdateLayout();
                }
                var referenceSection = security.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("settings-row-label"));
                foreach (var (tab, panelName, titleKey) in new[]
                {
                    (0, "AppearanceSettingsPanel", AvaloniaStringKeys.Appearance),
                    (1, "BrandIconsPanel", AvaloniaStringKeys.IconPacks)
                })
                {
                    tabs.SelectedIndex = tab;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var panel = Named<StackPanel>(window, panelName);
                    var title = panel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == localization.GetString(titleKey));
                    Assert.True(referenceTitle.FontSize == title.FontSize,
                        $"{panelName} page title={title.FontSize}, Security={referenceTitle.FontSize}, reference section={referenceSection.FontSize} ({width}, {culture}).");
                    Assert.True(title.TextLayout.Width <= title.Bounds.Width + 1);
                    Assert.True(title.TextLayout.Height <= title.Bounds.Height + 1);
                    foreach (var section in panel.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("settings-card-title")))
                    {
                        Assert.True(referenceSection.FontSize == section.FontSize,
                            $"{panelName} section '{section.Text}'={section.FontSize}, Security section={referenceSection.FontSize}, page={title.FontSize} ({width}, {culture}).");
                        Assert.True(section.TextLayout.Width <= section.Bounds.Width + 1 || !section.IsEffectivelyVisible);
                    }
                }
            }
        }
        finally { window.Close(); }
    }
}
