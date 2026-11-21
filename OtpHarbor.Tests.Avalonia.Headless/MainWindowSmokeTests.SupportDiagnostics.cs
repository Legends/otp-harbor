using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentResults;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Diagnostics;
using TOTP.Avalonia.Shared.Import;
using TOTP.Avalonia.Shared.Storage;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 416)]
    [InlineData(900, 720)]
    [InlineData(1440, 820)]
    public async Task SupportDiagnostics_SummaryAndLogActionsReflowWithLocalizedContent(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(height, 480) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        var reporter = new SupportTestReporter();
        var storage = new StorageLocationsViewModel(new SupportTestLocations(), new SupportTestClipboard(), localization.GetString);
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization,
            gitHubIssueReporter: reporter, autoSaveDelay: TimeSpan.FromHours(1), storageLocations: storage);
        var source = new SummaryDiagnostics();
        using var diagnostics = new DiagnosticsViewModel(source, localization,
            environmentProvider: new SummaryEnvironment(), logSwitch: new SummaryLogSwitch());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 4;
            window.FindControl<ContentControl>("MiscSettingsHost")!.Content = settings;
            window.FindControl<ContentControl>("SupportDiagnosticsHost")!.Content = diagnostics;
            window.FindControl<ContentControl>("StorageLocationsHost")!.Content = storage;
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                window.RequestedThemeVariant = theme;
                await diagnostics.RefreshAsync();
                window.UpdateLayout();
                var summary = window.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "DiagnosticsSummaryBorder");
                var primary = summary.GetVisualDescendants().OfType<ItemsControl>().Single(value => value.Name == "DiagnosticsPrimaryDetails");
                var secondary = summary.GetVisualDescendants().OfType<ItemsControl>().Single(value => value.Name == "DiagnosticsSecondaryDetails");
                var values = summary.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Where(value => value.Classes.Contains("diagnostic-value")).ToArray();
                Assert.Equal(diagnostics.PrimaryDetails.Concat(diagnostics.SecondaryDetails).Select(row => row.Value), values.Select(value => value.Text));
                Assert.Equal(6, values.Length);
                Assert.Contains("1920 × 1080 (150%)", values.Select(value => value.Text));
                var stacked = summary.Bounds.Width <= 600;
                Assert.Equal(stacked ? 0 : 2, Grid.GetColumn(secondary));
                Assert.Equal(stacked ? 2 : 0, Grid.GetRow(secondary));
                var divider = summary.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "DiagnosticsColumnDivider");
                Assert.Equal(!stacked, divider.IsVisible);
                foreach (var value in values)
                {
                    Assert.True(value.Bounds.Width > 0);
                    var left = value.TranslatePoint(default, summary)!.Value.X;
                    Assert.InRange(left + value.Bounds.Width, 0, summary.Bounds.Width + 1);
                }
                var card = window.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "SupportDiagnosticsPanel");
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(),
                    value => value.Text == localization.GetString(AvaloniaStringKeys.DiagnosticDescription));
                Assert.Contains(card.GetVisualDescendants().OfType<SymbolIcon>(), value => value.Kind == SymbolIconKind.Layers);
                var refresh = card.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "RefreshDiagnosticsButton");
                Assert.Same(diagnostics.RefreshCommand, refresh.Command);
                var details = card.GetVisualDescendants().OfType<Expander>().Single();
                details.IsExpanded = true;
                window.UpdateLayout();
                Assert.Equal(diagnostics.SupportInformation, details.GetVisualDescendants().OfType<TextBox>().Single().Text);
                details.IsExpanded = false;

                var email = window.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "EmailDiagnosticLogButton");
                var folder = window.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "OpenLogFolderButton");
                Assert.Same(settings.EmailDiagnosticLogCommand, email.Command);
                Assert.Same(settings.OpenLogFolderCommand, folder.Command);
                Assert.IsType<WrapPanel>(email.Parent);
                Assert.Same(email.Parent, folder.Parent);
                AssertContentSized(email);
                AssertContentSized(folder);
                if (email.Bounds.Width + email.Margin.Left + email.Margin.Right + folder.Bounds.Width + folder.Margin.Left + folder.Margin.Right
                    <= ((Control)email.Parent!).Bounds.Width + 1)
                    Assert.Equal(email.Bounds.Y, folder.Bounds.Y, 1);
                else Assert.True(folder.Bounds.Y >= email.Bounds.Bottom);
                Assert.Contains("secondary", folder.Classes);
                var page = Named<StackPanel>(window, "SupportSettingsPanel");
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.SupportDescription));
                var logCard = Named<Border>(page, "MiscSettingsPanel");
                var reportCard = Named<Border>(page, "ProblemReportSettingsPanel");
                var dataCard = Named<Border>(page, "ApplicationDataSettingsPanel");
                var cards = new[] { logCard, reportCard, card, dataCard };
                Assert.All(cards, value => Assert.Contains("settings-card", value.Classes));
                Assert.All(cards.Zip(cards.Skip(1)), pair => Assert.True(
                    pair.First.TranslatePoint(new Point(0, pair.First.Bounds.Height), page)!.Value.Y
                    < pair.Second.TranslatePoint(default, page)!.Value.Y));
                Assert.Contains("settings-card", logCard.Classes);
                Assert.Contains("settings-card", reportCard.Classes);
                Assert.Contains(logCard.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.SupportLogsNotice));
                Assert.Contains(reportCard.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.SupportReportPublicHelp));
                Assert.Contains(reportCard.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.SupportReportWarning));
                var picker = Named<ComboBox>(logCard, "MinimumLoggingLevelPicker");
                Assert.Same(settings.LogLevelOptions, picker.ItemsSource);
                Assert.Equal(settings.SelectedLogLevel, picker.SelectedItem);
                picker.SelectedItem = settings.LogLevelOptions.Single(option => option.Level == AppLogLevel.Error);
                Assert.Equal(AppLogLevel.Error, settings.MinimumLogLevel);
                Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.DiagnosticLogLevelError));
                Assert.Equal(page.Bounds.Width <= 520 ? 1 : 0, Grid.GetRow(picker));
                if (page.Bounds.Width <= 520) Assert.Equal(HorizontalAlignment.Left, picker.HorizontalAlignment);
                Assert.Equal(page.Bounds.Width <= 760 ? 1 : 0, Grid.GetRow((WrapPanel)email.Parent!));
                var report = Named<Button>(reportCard, "ReportOnGitHubButton");
                Assert.Same(settings.ReportOnGitHubCommand, report.Command);
                Assert.Equal(page.Bounds.Width <= 760 ? 2 : 0, Grid.GetRow(report));
                Assert.Equal(page.Bounds.Width <= 760 ? HorizontalAlignment.Left : HorizontalAlignment.Right, report.HorizontalAlignment);
                AssertContentSized(report);
                window.MouseMove(new Point(-1, -1));
                foreach (var state in new[] { "normal", "hover", "focus", "disabled" })
                {
                    if (state == "hover") MovePointerTo(window, report);
                    if (state == "focus") report.Focus();
                    report.IsEnabled = state != "disabled";
                    window.UpdateLayout();
                    var label = Assert.IsType<SymbolIconLabel>(report.Content);
                    var center = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), report)!.Value.Y;
                    Assert.InRange(Math.Abs(center - report.Bounds.Height / 2), 0, 1);
                }
                report.IsEnabled = true;
                foreach (var action in new[] { email, folder, report, refresh })
                {
                    Assert.Equal(VerticalAlignment.Center, action.VerticalContentAlignment);
                    Assert.True(action.Bounds.Height >= 40);
                    var left = action.TranslatePoint(default, page)!.Value.X;
                    Assert.InRange(left, 0, page.Bounds.Width);
                    Assert.InRange(left + action.Bounds.Width, 0, page.Bounds.Width + 1);
                    if (action.Content is SymbolIconLabel label)
                    {
                        Assert.True(label.DesiredSize.Width <= action.Bounds.Width - action.Padding.Left - action.Padding.Right + 1);
                        var center = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), action)!.Value.Y;
                        Assert.InRange(Math.Abs(center - action.Bounds.Height / 2), 0, 1);
                    }
                }
                foreach (var key in new[] { AvaloniaStringKeys.SupportLogsTitle, AvaloniaStringKeys.SupportReportTitle })
                    Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(key));
                foreach (var text in page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("settings-help")))
                    Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1);
                foreach (var canOpen in new[] { true, false })
                {
                    reporter.CanOpen = canOpen;
                    ClickControl(window, report);
                    window.UpdateLayout();
                    var notice = Named<NotificationBanner>(logCard, "SupportLogActionStatus");
                    Assert.True(notice.IsVisible);
                    Assert.Equal(localization.GetString(canOpen ? AvaloniaStringKeys.GitHubReportOpened : AvaloniaStringKeys.GitHubReportFailed), notice.Text);
                    Assert.Equal(canOpen ? NotificationSeverity.Success : NotificationSeverity.Error, notice.Severity);
                    Assert.Equal(1, reporter.Calls);
                    reporter.Calls = 0;
                }
                settings.LogFolderNotification.Clear();
                source.Fail = true;
                await diagnostics.RefreshAsync();
                window.UpdateLayout();
                var error = Named<NotificationBanner>(card, "SupportDiagnosticsStatus");
                Assert.True(error.IsVisible);
                Assert.Equal(localization.GetString(AvaloniaStringKeys.DiagnosticRefreshFailed), error.Text);
                Assert.Equal(NotificationSeverity.Error, error.Severity);
                Assert.Empty(diagnostics.SupportInformation);
                source.Fail = false;
                diagnostics.Notification.Clear();
                await diagnostics.RefreshAsync();
                window.UpdateLayout();
                var scroller = window.FindControl<ScrollViewer>("SupportSettingsScroll")!;
                Assert.Equal(ScrollBarVisibility.Hidden, scroller.VerticalScrollBarVisibility);
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                refresh.BringIntoView();
                window.UpdateLayout();
                Assert.InRange(refresh.TranslatePoint(new Point(0, refresh.Bounds.Height), scroller)!.Value.Y, 1, scroller.Viewport.Height + 1);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(800, 620)]
    public void IconAndBackupActions_RespectRequestedAlignmentAndWidth(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.Show();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                localization.ApplyCulture(language);
                tabs.SelectedIndex = 1;
                window.FindControl<ContentControl>("BrandIconSettingsHost")!.Content = settings;
                window.UpdateLayout();
                var imports = window.GetVisualDescendants().OfType<Button>()
                    .Where(value => value.Name is "BrandIconsImportButton" or "BrandIconsResetButton").ToArray();
                Assert.Equal(2, imports.Length);
                var tab = Assert.IsType<TabItem>(tabs.Items[1]);
                var caption = Assert.Single(Assert.IsType<Grid>(tab.Header).Children.OfType<TextBlock>());
                Assert.Equal(language switch
                {
                    "de" => "Icon-Paket", "fr" => "Pack d’icônes", "es" => "Paquete de iconos", _ => "Icon pack"
                }, caption.Text);
                var iconActions = Assert.IsAssignableFrom<Control>(imports[0].Parent);
                Assert.Same(iconActions, imports[1].Parent);
                Assert.Equal(HorizontalAlignment.Stretch, iconActions.HorizontalAlignment);
                var remove = imports.Single(value => value.Name == "BrandIconsResetButton");
                var import = imports.Single(value => value.Name == "BrandIconsImportButton");
                iconActions.Classes.Add("installed");
                remove.IsVisible = true;
                window.UpdateLayout();
                Assert.Equal(import.Bounds.Width, remove.Bounds.Width, 1);
                var stacked = window.GetVisualDescendants().OfType<StackPanel>().Single(value => value.Name == "BrandIconsPanel").Bounds.Width <= 560;
                Assert.Equal(stacked ? 0 : 1, Grid.GetColumn(remove));
                Assert.Equal(stacked ? 1 : 0, Grid.GetRow(remove));
                foreach (var button in imports)
                {
                    Assert.Equal(HorizontalAlignment.Stretch, button.HorizontalAlignment);
                    Assert.True(button.Bounds.Width > 0);
                    var left = button.TranslatePoint(default, iconActions)!.Value.X;
                    Assert.InRange(left + button.Bounds.Width, 0, iconActions.Bounds.Width + 1);
                    var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                    Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1);
                    if (stacked) Assert.Equal(iconActions.Bounds.Width, button.Bounds.Width, 1);
                }
                remove.IsVisible = false;
                iconActions.Classes.Remove("installed");
                window.UpdateLayout();
                Assert.Equal(iconActions.Bounds.Width, import.Bounds.Width, 1);
                iconActions.Classes.Add("installed");
                remove.IsVisible = true;
                window.UpdateLayout();
                Assert.Equal(import.Bounds.Width, remove.Bounds.Width, 1);

                tabs.SelectedIndex = 3;
                var host = window.FindControl<ContentControl>("ImportExportSettingsHost")!;
                var template = host.ContentTemplate!;
                var content = (Control)template.Build(null)!;
                host.ContentTemplate = null;
                host.Content = content;
                window.UpdateLayout();
                var importPanel = content.GetVisualDescendants().OfType<SettingsImportPanel>().Single();
                var file = new NoAction();
                var qr = new NoAction();
                importPanel.DataContext = new AuthenticatorImportViewModel(localization.GetString, file, qr);
                window.UpdateLayout();
                foreach (var command in new[] { file, qr })
                {
                    var button = importPanel.GetVisualDescendants().OfType<Button>().Single(value => ReferenceEquals(value.Command, command));
                    Assert.Equal(HorizontalAlignment.Stretch, button.HorizontalAlignment);
                    Assert.True(button.Bounds.Width > 0);
                }
                var export = content.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "EncryptedBackupExportButton");
                AssertContentSized(export);
                Assert.Equal(HorizontalAlignment.Left, export.HorizontalAlignment);
                var scroll = Assert.IsType<ScrollViewer>(content);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                host.Content = null;
                host.ContentTemplate = template;
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SharedImportActions_FillAvailableWidthAndRetainCommands()
    {
        var localization = new AvaloniaLocalizationService(new ResourceDictionary(), new AvaloniaStringCatalog());
        var file = new NoAction();
        var qr = new NoAction();
        var model = new AuthenticatorImportViewModel(localization.GetString, file, qr);
        var stretch = new AuthenticatorImportPanel { DataContext = model };
        var window = new Window { Width = 520, Height = 620, Content = new ScrollViewer
        {
            Content = stretch
        } };
        try
        {
            window.Show();
            window.UpdateLayout();
            foreach (var command in new[] { file, qr })
            {
                var defaultButton = stretch.GetVisualDescendants().OfType<Button>().Single(value => ReferenceEquals(value.Command, command));
                AssertStretchedAction(defaultButton);
            }
        }
        finally { window.Close(); }
    }

    private static void AssertContentSized(Button button)
    {
        var expected = NaturalActionWidth(button);
        Assert.InRange(button.Bounds.Width, expected - 1, expected + 1);
    }

    private static double NaturalActionWidth(Button button) => Assert.IsType<SymbolIconLabel>(button.Content).DesiredSize.Width
        + button.Padding.Left + button.Padding.Right + button.BorderThickness.Left + button.BorderThickness.Right;

    private static void AssertStretchedAction(Button button)
    {
        Assert.Equal(HorizontalAlignment.Stretch, button.HorizontalAlignment);
        var parent = Assert.IsAssignableFrom<Control>(button.Parent);
        Assert.InRange(Math.Abs(button.Bounds.Width - parent.Bounds.Width), 0, 1);
    }

    private static void AssertCenteredContent(Button button)
    {
        AssertContentSized(button);
        Assert.Equal(HorizontalAlignment.Center, button.HorizontalAlignment);
        var parent = Assert.IsAssignableFrom<Control>(button.Parent);
        Assert.InRange(Math.Abs(button.Bounds.Center.X - parent.Bounds.Width / 2), 0, 1);
    }

    private sealed class SummaryDiagnostics : ISupportDiagnosticsService
    {
        public bool Fail { get; set; }
        public SupportDiagnosticsSnapshot Capture() => Fail ? throw new IOException("private boundary details")
            : new("3.2.1", "Windows 11", "X64", ".NET 10.0", true, []);
    }

    private sealed class SupportTestReporter : IGitHubIssueReporter
    {
        public bool CanOpen { get; set; }
        public int Calls { get; set; }
        public Task<bool> OpenAsync() { Calls++; return Task.FromResult(CanOpen); }
    }

    private sealed class SupportTestLocations : IApplicationStorageLocations
    {
        public event EventHandler? Changed { add { } remove { } }
        public IReadOnlyList<ApplicationStorageLocation> GetLocations() =>
        [new(ApplicationStorageLocationKind.DataDirectory, @"C:\Users\Demo\AppData\Local\OTP Harbor\a long application data directory", @"C:\Users\Demo\AppData\Local\OTP Harbor\a long application data directory")];
    }

    private sealed class SupportTestClipboard : IAsyncClipboardService
    {
        public ClipboardCapabilities Capabilities => ClipboardCapabilities.WriteText;
        public Task<Result> CopyAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Result.Ok());
        public Task<Result> CopyAndScheduleClearAsync(string text, TimeSpan duration, CancellationToken cancellationToken = default) => CopyAsync(text, cancellationToken);
    }

    private sealed class SummaryEnvironment : IDiagnosticEnvironmentProvider
    {
        public DiagnosticEnvironment Capture() => new(DateTimeOffset.UnixEpoch, "3.2.1", "synthetic", "Release", "direct",
            "Windows 11 (23H2)", "X64", ".NET 10.0", "800 × 620; 1.5×", true, "en", null)
            { ScreenDisplay = "1920 × 1080 (150%)" };
    }

    private sealed class SummaryLogSwitch : ILogSwitchService
    {
        public AppLogLevel MinimumLevel { get; private set; } = AppLogLevel.Information;
        public bool IsCliOverrideActive { get; set; }
        public AppLogLevel GetLevel() => MinimumLevel;
        public void SetLevel(AppLogLevel level) => MinimumLevel = level;
    }

    private sealed class NoAction : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => false;
        public void Execute(object? parameter) { }
    }
}
