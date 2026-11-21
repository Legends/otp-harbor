using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentResults;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.About;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Diagnostics;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 416)]
    [InlineData(900, 720)]
    [InlineData(1440, 820)]
    public async Task AboutCards_ShowRealMetadataSafeLinksAndDistributionSpecificUpdates(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(height, 480) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        var opened = new List<Uri>();
        var canOpen = true;
        var about = new AboutViewModel(localization.GetString, () => AboutTestEnvironment.InstalledVersion,
            uri => { opened.Add(uri); return Task.FromResult(canOpen); });
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization, about: about);
        using var diagnostics = new DiagnosticsViewModel(new SummaryDiagnostics(), localization,
            environmentProvider: new AboutTestEnvironment(), logSwitch: new SummaryLogSwitch());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 6;
            var identity = window.FindControl<SettingsAboutIdentityPanel>("AboutIdentityPanel")!;
            identity.DataContext = about;
            window.FindControl<ContentControl>("AboutDiagnosticsHost")!.Content = diagnostics;
            window.FindControl<ContentControl>("AboutCreditsHost")!.Content = about;
            await diagnostics.RefreshAsync();
            foreach (var store in new[] { false, true })
            foreach (var locale in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(locale);
                window.RequestedThemeVariant = theme;
                var service = DispatchProxy.Create<IPortableUpdateService, AboutUpdateBoundary>();
                var boundary = (AboutUpdateBoundary)(object)service;
                using var updates = new UpdateCheckViewModel(service, DispatchProxy.Create<IUpdateInstallerLauncher, AboutUpdateBoundary>(),
                    localization, isStoreDistribution: store, installedVersionProvider: () => AboutTestEnvironment.InstalledVersion);
                var updateHost = window.FindControl<ContentControl>("SoftwareUpdatesHost")!;
                updateHost.Content = updates;
                updateHost.IsVisible = updates.IsSoftwareUpdatesVisible;
                window.UpdateLayout();
                var page = window.FindControl<StackPanel>("AboutSettingsPanel")!;
                var scroll = window.FindControl<ScrollViewer>("AboutSettingsScroll")!;
                Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
                Assert.Equal(AboutTestEnvironment.InstalledVersion, Named<SelectableTextBlock>(identity, "AboutInstalledVersion").Text);
                Assert.Contains(identity.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == localization.GetString(AvaloniaStringKeys.AboutDescription));
                var repository = Named<Button>(identity, "AboutRepositoryButton");
                var website = Named<Button>(identity, "AboutWebsiteButton");
                var license = Named<Button>(identity, "AboutLicenseButton");
                Assert.Same(about.RepositoryEntry.Command, repository.Command);
                Assert.Same(about.WebsiteEntry.Command, website.Command);
                Assert.Same(about.LicenseEntry.Command, license.Command);
                Assert.Equal(identity.Bounds.Width <= 620 ? 1 : 0, Grid.GetRow(website));
                Assert.Equal(identity.Bounds.Width <= 620 ? 2 : 0, Grid.GetRow(license));
                if (identity.Bounds.Width > 620)
                {
                    Assert.Equal(repository.Bounds.Height, website.Bounds.Height, 1);
                    Assert.Equal(repository.Bounds.Height, license.Bounds.Height, 1);
                }
                foreach (var pair in new[] { (repository, about.RepositoryEntry), (website, about.WebsiteEntry), (license, about.LicenseEntry) })
                {
                    Assert.Equal(pair.Item2.Title, AutomationProperties.GetName(pair.Item1));
                    Assert.Contains(pair.Item1.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == pair.Item2.Title);
                    Assert.Equal(VerticalAlignment.Center, pair.Item1.VerticalContentAlignment);
                    Assert.True(pair.Item1.Bounds.Height >= 64);
                    ClickControl(window, pair.Item1);
                }
                Assert.Equal(new[] { AboutViewModel.RepositoryUri, about.WebsiteUri, AboutViewModel.LicenseUri }, opened);
                opened.Clear();
                var details = Named<Border>(window, "AboutAppDetailsCard");
                var values = details.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
                Assert.Equal(diagnostics.PrimaryDetails.Concat(diagnostics.SecondaryDetails).Select(value => value.Value), values.Select(value => value.Text));
                Assert.Equal(6, values.Length);
                Assert.Contains(values, value => value.Text == AboutTestEnvironment.InstalledVersion);
                var summary = Named<Border>(details, "AboutDiagnosticsSummary");
                Assert.Equal(summary.Bounds.Width <= 600 ? 2 : 0, Grid.GetRow(Named<ItemsControl>(details, "AboutSecondaryDetails")));
                var credits = Named<Button>(window, "AboutViewCreditsButton");
                Assert.Same(about.CreditsCommand, credits.Command);
                Assert.Equal(localization.GetString(AvaloniaStringKeys.AboutViewCredits), AutomationProperties.GetName(credits));
                Assert.Equal(page.Bounds.Width <= 600 ? 1 : 0, Grid.GetRow(credits));
                ClickControl(window, credits);
                Assert.Equal(AboutViewModel.CreditsUri, Assert.Single(opened));
                opened.Clear();
                Assert.False(updates.IsUpToDate);
                await updates.CheckAsync();
                window.UpdateLayout();
                if (store)
                {
                    Assert.Equal(0, boundary.CheckCalls);
                    Assert.False(updateHost.IsVisible);
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), value => value.Name == "AboutSoftwareUpdatesCard" && value.IsEffectivelyVisible);
                    var identityCard = window.FindControl<Border>("AppInformationPanel")!;
                    var detailsHost = window.FindControl<ContentControl>("AboutDiagnosticsHost")!;
                    Assert.Equal(page.Spacing, detailsHost.TranslatePoint(default, page)!.Value.Y - identityCard.Bounds.Bottom, 1);
                }
                else
                {
                    Assert.Equal(1, boundary.CheckCalls);
                    Assert.True(updates.IsUpToDate);
                    Assert.True(Named<Grid>(window, "AboutUpToDateStatus").IsVisible);
                    Assert.Equal(string.Format(localization.GetString(AvaloniaStringKeys.AboutUpdatesCurrentVersion), AboutTestEnvironment.InstalledVersion), Named<TextBlock>(window, "AboutCurrentVersionStatus").Text);
                    Assert.Same(updates.CheckCommand, Named<Button>(window, "AboutCheckUpdatesButton").Command);
                    Assert.Same(updates.DownloadCommand, Named<Button>(window, "AboutDownloadUpdateButton").Command);
                    Assert.Same(updates.InstallCommand, Named<Button>(window, "AboutInstallUpdateButton").Command);
                    Assert.Same(updates.CancelCommand, Named<Button>(window, "AboutCancelUpdateButton").Command);
                    var updateStatus = Named<Grid>(window, "AboutUpdateStatus");
                    Assert.Equal(page.Bounds.Width <= 600 ? 1 : 0, Grid.GetRow(updateStatus));
                    Assert.Equal(page.Bounds.Width <= 600 ? 0 : 2, Grid.GetColumn(updateStatus));
                    boundary.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var checking = updates.CheckAsync();
                    window.UpdateLayout();
                    Assert.False(Named<Grid>(window, "AboutUpToDateStatus").IsVisible);
                    Assert.True(Named<Button>(window, "AboutCancelUpdateButton").IsVisible);
                    boundary.Pending.SetResult(Result.Ok(new PortableUpdateCheckResult(PortableUpdateCheckStatus.NoUpdate)));
                    await checking;
                    boundary.Pending = null;
                }
                foreach (var state in new[] { "normal", "hover", "focus", "disabled" })
                {
                    window.MouseMove(new Point(-1, -1));
                    if (state == "hover") MovePointerTo(window, repository);
                    if (state == "focus") repository.Focus(NavigationMethod.Tab);
                    repository.IsEnabled = state != "disabled";
                    window.UpdateLayout();
                    var content = Assert.IsType<Grid>(repository.Content);
                    var center = content.TranslatePoint(new Point(0, content.Bounds.Height / 2), repository)!.Value.Y;
                    Assert.InRange(Math.Abs(center - repository.Bounds.Height / 2), 0, 1);
                }
                repository.IsEnabled = true;
                foreach (var text in page.GetVisualDescendants().OfType<TextBlock>().Where(value => value.IsEffectivelyVisible))
                {
                    Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1);
                    var left = text.TranslatePoint(default, page)!.Value.X;
                    Assert.InRange(left + text.Bounds.Width, 0, page.Bounds.Width + 1);
                }
                credits.BringIntoView();
                window.UpdateLayout();
                Assert.InRange(credits.TranslatePoint(new Point(0, credits.Bounds.Height), scroll)!.Value.Y, 0, scroll.Viewport.Height + 1);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                canOpen = false;
                ClickControl(window, credits);
                Assert.True(about.HasStatus);
                Assert.Equal(localization.GetString(AboutStringKeys.ActionFailed), about.Status);
                canOpen = true;
                ClickControl(window, credits);
                opened.Clear();
            }
        }
        finally { window.Close(); }
    }

    private sealed class AboutTestEnvironment : IDiagnosticEnvironmentProvider
    {
        public const string InstalledVersion = "3.2.1.65535";
        public DiagnosticEnvironment Capture() => new SummaryEnvironment().Capture() with { AppVersion = InstalledVersion };
    }

    public class AboutUpdateBoundary : DispatchProxy
    {
        public int CheckCalls { get; private set; }
        public TaskCompletionSource<Result<PortableUpdateCheckResult>>? Pending { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "CheckAsync")
            {
                CheckCalls++;
                return Pending?.Task ?? Task.FromResult(Result.Ok(new PortableUpdateCheckResult(PortableUpdateCheckStatus.NoUpdate)));
            }
            if (method?.Name == "get_IsSupported") return true;
            throw new InvalidOperationException($"Unexpected About test boundary call: {method?.Name}");
        }
    }
}
