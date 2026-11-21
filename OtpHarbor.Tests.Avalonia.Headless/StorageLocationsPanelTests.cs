using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentResults;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Storage;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class StorageLocationsPanelTests
{
    [AvaloniaTheory]
    [InlineData(360, false, "de", false)]
    [InlineData(430, true, "fr", false)]
    [InlineData(360, true, "fr", true)]
    [InlineData(520, false, "es", true)]
    [InlineData(720, true, "en", true)]
    public void PathsAndActions_FitConstrainedViewportAndRemainScrollable(int width, bool dark, string language, bool folders)
    {
        using var model = CreateModel(language, folders);
        var panel = new StorageLocationsPanel { DataContext = model, Margin = new Thickness(32) };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
        var window = new Window
        {
            Width = width, Height = 120, Content = scroller,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Single(model.Locations);
            Assert.Equal(model.Locations.Select(value => value.Path),
                panel.GetVisualDescendants().OfType<SelectableTextBlock>().Select(value => value.Text));
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
            var buttons = panel.GetVisualDescendants().OfType<Button>().Where(value => value.IsVisible).ToArray();
            Assert.Equal(model.Locations.Count * (folders ? 2 : 1), buttons.Length);
            foreach (var button in buttons)
            {
                Assert.True(button.Bounds.Height >= 44);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.IsType<SymbolIcon>(button.Content);
                Assert.IsType<string>(ToolTip.GetTip(button));
                var origin = button.TranslatePoint(default, panel)!.Value;
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= panel.Bounds.Width + 1);
            }
            var paths = panel.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
            var label = Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>(),
                value => value.Text == model.Locations[0].Label);
            var labelGrid = Assert.IsType<Grid>(label.Parent);
            Assert.Equal(SymbolIconKind.Folder, Assert.Single(labelGrid.Children.OfType<SymbolIcon>()).Kind);
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(),
                value => value.Text is "Storage locations" or "Speicherorte" or "Local storage" or "Lokaler Speicher");
            for (var index = 0; index < paths.Length; index++)
            {
                var field = Assert.IsType<Border>(paths[index].Parent);
                Assert.NotNull(field.Background);
                Assert.Equal(32, field.Bounds.Height);
                if (!dark) Assert.Equal(Color.Parse("#EFEFEF"), Assert.IsAssignableFrom<ISolidColorBrush>(field.Background).Color);
                Assert.Equal(model.Locations[index].Path, ToolTip.GetTip(paths[index]));
                Assert.Single(paths[index].TextLayout.TextLines);
                Assert.True(field.Bounds.Width >= 120);
                var top = field.TranslatePoint(default, panel)!.Value;
                foreach (var button in buttons.Skip(index * (folders ? 2 : 1)).Take(folders ? 2 : 1))
                {
                    var center = button.TranslatePoint(new Point(0, button.Bounds.Height / 2), panel)!.Value;
                    Assert.True(center.X >= top.X + field.Bounds.Width);
                    Assert.InRange(center.Y, top.Y, top.Y + field.Bounds.Height);
                }
            }
            Assert.True(paths[0].TextLayout.TextLines[0].HasCollapsed);
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            window.UpdateLayout();
            Assert.True(scroller.Offset.Y > 0);
            var lastButton = buttons[^1];
            var bottom = lastButton.TranslatePoint(new Point(0, lastButton.Bounds.Height), scroller)!.Value.Y;
            Assert.InRange(bottom, 0, scroller.Viewport.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 416, "de")]
    [InlineData(520, 620, "fr")]
    [InlineData(900, 720, "es")]
    [InlineData(1440, 820, "en")]
    public async Task DesktopSupportTab_RendersDiagnosticsAndScrollableStorageLocations(int width, int height, string locale)
    {
        using var model = CreateModel(locale);
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(height, 480) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        localization.ApplyCulture(locale);
        using var diagnostics = new DiagnosticsViewModel(new TestDiagnostics(), localization);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
            tabs.SelectedIndex = 4;
            window.UpdateLayout();
            var host = window.FindControl<ContentControl>("StorageLocationsHost")!;
            host.Content = model;
            window.UpdateLayout();
            var panel = Assert.Single(host.GetVisualDescendants().OfType<SettingsStorageLocationsPanel>());
            Assert.Same(model, panel.DataContext);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), value => value.Text == model.Locations[0].Label);
            Assert.Single(panel.GetVisualDescendants().OfType<SelectableTextBlock>());
            var diagnosticsHost = window.FindControl<ContentControl>("SupportDiagnosticsHost")!;
            diagnosticsHost.Content = diagnostics;
            await diagnostics.RefreshAsync();
            window.UpdateLayout();
            var values = diagnosticsHost.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Where(value => value.Classes.Contains("diagnostic-value")).ToArray();
            Assert.Equal(6, values.Length);
            Assert.Contains(values, value => value.Text == diagnostics.ApplicationVersion);
            var details = Assert.Single(diagnosticsHost.GetVisualDescendants().OfType<Expander>());
            Assert.False(details.IsExpanded);
            details.IsExpanded = true;
            window.UpdateLayout();
            var output = Assert.Single(diagnosticsHost.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(diagnostics.SupportInformation, output.Text);
            Assert.Contains("OTP Harbor 3.0.3", output.Text);
            var scroller = window.FindControl<ScrollViewer>("SupportSettingsScroll")!;
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
            var lastButton = panel.GetVisualDescendants().OfType<Button>().Last();
            lastButton.BringIntoView();
            window.UpdateLayout();
            var bottom = lastButton.TranslatePoint(new Point(0, lastButton.Bounds.Height), scroller)!.Value.Y;
            Assert.InRange(bottom, 1, scroller.Viewport.Height + 1);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                window.UpdateLayout();
                var card = host.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "ApplicationDataSettingsPanel");
                Assert.Contains("settings-card", card.Classes);
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.SupportApplicationDataTitle));
                var path = Assert.Single(panel.GetVisualDescendants().OfType<SelectableTextBlock>());
                Assert.Equal(model.Locations[0].Path, path.Text);
                Assert.Equal(path.Text, ToolTip.GetTip(path));
                Assert.Single(path.TextLayout.TextLines);
                Assert.Equal(TextTrimming.CharacterEllipsis, path.TextTrimming);
                Assert.True(path.Bounds.Width >= 120);
                var field = Assert.IsType<Border>(path.Parent);
                Assert.Equal(panel.Bounds.Width <= 600 ? 1 : 0, Grid.GetRow(field));
                foreach (var button in panel.GetVisualDescendants().OfType<Button>())
                {
                    Assert.True(button.Bounds.Height >= 44);
                    Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, button.VerticalContentAlignment);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    var left = button.TranslatePoint(default, panel)!.Value.X;
                    Assert.InRange(left + button.Bounds.Width, 0, panel.Bounds.Width + 1);
                }
                var copy = panel.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "CopyStorageSettingsPathButton");
                var open = panel.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "OpenStorageSettingsFolderButton");
                Assert.Same(model.Locations[0].CopyCommand, copy.Command);
                Assert.Same(model.Locations[0].OpenFolderCommand, open.Command);
                copy.Command!.Execute(null);
                window.UpdateLayout();
                var feedback = Assert.Single(panel.GetVisualDescendants().OfType<NotificationBanner>());
                Assert.True(feedback.IsVisible);
                Assert.Equal(localization.GetString(StorageLocationStringKeys.CopySucceeded), feedback.Text);
                open.Command!.Execute(null);
                window.UpdateLayout();
                Assert.Equal(localization.GetString(StorageLocationStringKeys.FolderOpened), feedback.Text);
                Assert.Equal(NotificationSeverity.Success, feedback.Severity);
                lastButton.BringIntoView();
                window.UpdateLayout();
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
            }
        }
        finally { window.Close(); }
    }

    private sealed class TestDiagnostics : ISupportDiagnosticsService
    {
        public SupportDiagnosticsSnapshot Capture() => new("3.0.3", "Windows", "X64", ".NET 10.0", true, []);
    }

    private static StorageLocationsViewModel CreateModel(string language, bool folders = true)
    {
        var catalog = new AvaloniaStringCatalog();
        return new StorageLocationsViewModel(new Locations(), new Clipboard(),
            key => catalog.Get(key, CultureInfo.GetCultureInfo(language)), folders ? new FolderLauncher() : null);
    }

    private sealed class Locations : IApplicationStorageLocations
    {
        public event EventHandler? Changed { add { } remove { } }
        public IReadOnlyList<ApplicationStorageLocation> GetLocations() =>
        [
            new(ApplicationStorageLocationKind.DataDirectory,
                "/home/demo/.var/app/io.github.legends.otp-harbor/data/OTP Harbor/a-very-long-custom-app-data-directory",
                "/home/demo/.var/app/io.github.legends.otp-harbor/data/OTP Harbor/a-very-long-custom-app-data-directory"),
            new(ApplicationStorageLocationKind.Vault,
                "/home/demo/.var/app/io.github.legends.otp-harbor/data/OTP Harbor/a-very-long-custom-vault-directory/master.vault",
                "/home/demo/.var/app/io.github.legends.otp-harbor/data/OTP Harbor/a-very-long-custom-vault-directory"),
            new(ApplicationStorageLocationKind.Preferences, "/home/demo/OTP Harbor/preferences.json", "/home/demo/OTP Harbor"),
            new(ApplicationStorageLocationKind.LogDirectory, "/home/demo/OTP Harbor/Logs", "/home/demo/OTP Harbor/Logs")
        ];
    }

    private sealed class Clipboard : IAsyncClipboardService
    {
        public ClipboardCapabilities Capabilities => ClipboardCapabilities.WriteText;
        public Task<Result> CopyAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Result.Ok());
        public Task<Result> CopyAndScheduleClearAsync(string text, TimeSpan duration, CancellationToken cancellationToken = default) => Task.FromResult(Result.Ok());
    }

    private sealed class FolderLauncher : IPlatformFolderLauncher
    {
        public Task<Result> OpenFolderAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Result.Ok());
    }
}
