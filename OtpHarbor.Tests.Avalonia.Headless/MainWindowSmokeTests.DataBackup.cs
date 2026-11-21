using System.Reflection;
using Avalonia;
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
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Backup;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(520, 416)]
    [InlineData(900, 720)]
    [InlineData(1440, 820)]
    public void DataBackupCards_ReflowAndPreserveConfirmedStateAndImportCommands(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(480, height) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        var backups = new LayoutBackups();
        var preferences = new TestSettingsService();
        using var model = new NativeFilePickerViewModel(
            DispatchProxy.Create<IAvaloniaFilePicker, UnexpectedBackupService>(),
            DispatchProxy.Create<IExportService, UnexpectedBackupService>(),
            DispatchProxy.Create<IAccountManager, UnexpectedBackupService>(),
            DispatchProxy.Create<IAccountImportService, UnexpectedBackupService>(),
            new LayoutDialogs(), new PasswordValidationService(),
            DispatchProxy.Create<IPlatformFileSecurity, UnexpectedBackupService>(), preferences,
            DispatchProxy.Create<IPlatformFolderLauncher, UnexpectedBackupService>(), localization,
            automaticBackup: backups);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 3;
            var host = window.FindControl<ContentControl>("ImportExportSettingsHost")!;
            host.Content = model;
            foreach (var locale in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(locale);
                window.RequestedThemeVariant = theme;
                foreach (var configured in new[] { false, true })
                {
                    backups.Publish(configured);
                    window.UpdateLayout();
                    var page = Named<StackPanel>(host, "DataBackupSettingsPanel");
                    var import = page.GetVisualDescendants().OfType<SettingsImportPanel>().Single();
                    var file = Named<Button>(import, "ImportFileButton");
                    var qr = Named<Button>(import, "ImportQrButton");
                    Assert.Same(model.ImportCommand, file.Command);
                    Assert.Same(model.ImportGoogleQrCommand, qr.Command);
                    Assert.Equal(HorizontalAlignment.Stretch, file.HorizontalAlignment);
                    Assert.Equal(HorizontalAlignment.Stretch, qr.HorizontalAlignment);
                    var actions = Named<Grid>(import, "ImportActionRow");
                    Assert.Equal(import.Bounds.Width <= 520 ? 1 : 0, Grid.GetRow(qr));
                    var source = Named<ComboBox>(import, "ExportHelpSource");
                    Assert.Same(model.MigrationImport.Sources, source.ItemsSource);
                    Assert.Equal(import.Bounds.Width <= 520 ? 1 : 0, Grid.GetRow(source));
                    var toggle = Named<CommandToggleSwitch>(page, "AutomaticBackupToggle");
                    Assert.Equal(configured, toggle.IsChecked);
                    var knob = Named<Border>(toggle, "SwitchKnobBounds");
                    var knobCenter = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), toggle)!.Value;
                    Assert.InRange(Math.Abs(knobCenter.Y - toggle.Bounds.Height / 2), 0, 1);
                    Assert.Same(model.ToggleAutomaticBackupCommand, toggle.Command);
                    Assert.True(toggle.IsEffectivelyEnabled);
                    var locationSection = Named<StackPanel>(page, "AutomaticBackupLocationSection");
                    Assert.True(locationSection.IsEffectivelyVisible);
                    Assert.Equal(configured, locationSection.IsEffectivelyEnabled);
                    Assert.Equal(configured ? 1 : .6, locationSection.Opacity, 2);
                    var folder = Named<SelectableTextBlock>(page, "AutomaticBackupFolderPath");
                    Assert.True(folder.IsEffectivelyVisible);
                    Assert.Equal(configured, folder.IsEffectivelyEnabled);
                    var change = Named<Button>(page, "ChangeAutomaticBackupFolderButton");
                    Assert.True(change.IsEffectivelyVisible);
                    Assert.Equal(configured, change.IsEffectivelyEnabled);
                    var health = Named<Expander>(page, "AutomaticBackupHealthDetails");
                    Assert.True(health.IsEffectivelyVisible);
                    Assert.Equal(configured, health.IsEffectivelyEnabled);
                    foreach (var choice in locationSection.GetVisualDescendants().OfType<RadioButton>())
                        Assert.Equal(configured, choice.IsEffectivelyEnabled);
                    if (configured)
                    {
                        Assert.Equal(backups.Destination!.Location, folder.Text);
                        Assert.True(Named<Expander>(page, "AutomaticBackupHealthDetails").IsExpanded);
                        var healthStatus = Named<TextBlock>(page, "AutomaticBackupHealthStatus");
                        Assert.True(healthStatus.TextLayout.Width <= healthStatus.Bounds.Width + 1);
                        Assert.Same(model.ConfigureAutomaticBackupCommand, Named<Button>(page, "ChangeAutomaticBackupFolderButton").Command);
                        Assert.Same(model.AutomaticBackupNowCommand, Named<Button>(page, "AutomaticBackupNowButton").Command);
                        Assert.Equal(page.Bounds.Width <= 560 ? 1 : 0, Grid.GetRow(Named<Button>(page, "ChangeAutomaticBackupFolderButton")));
                    }
                    else
                    {
                        Assert.True(string.IsNullOrEmpty(folder.Text));
                        var before = backups.ConfigureCalls;
                        change.BringIntoView();
                        window.UpdateLayout();
                        var viewport = change.GetVisualAncestors().OfType<ScrollViewer>().First();
                        Assert.InRange(change.TranslatePoint(new Point(change.Bounds.Width / 2, change.Bounds.Height / 2), viewport)!.Value.Y, 1, viewport.Viewport.Height);
                        var point = change.TranslatePoint(new Point(change.Bounds.Width / 2, change.Bounds.Height / 2), window)!.Value;
                        window.MouseDown(point, MouseButton.Left);
                        window.MouseUp(point, MouseButton.Left);
                        Assert.Equal(before, backups.ConfigureCalls);
                        Assert.False(change.Focus());
                    }
                    Assert.Same(model.ExportCommand, Named<Button>(page, "EncryptedBackupExportButton").Command);
                    foreach (var button in page.GetVisualDescendants().OfType<Button>().Where(value => value.IsEffectivelyVisible && value.Content is SymbolIconLabel))
                    {
                        Assert.True(button.Bounds.Height >= 40);
                        Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                        var left = button.TranslatePoint(default, page)!.Value.X;
                        Assert.InRange(left, 0, page.Bounds.Width);
                        Assert.True(left + button.Bounds.Width <= page.Bounds.Width + 1);
                        var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                        Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1, label.Text);
                        var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), button)!.Value;
                        Assert.InRange(Math.Abs(center.Y - button.Bounds.Height / 2), 0, 1);
                    }
                    var scroll = Named<ScrollViewer>(host, "DataBackupSettingsScroll");
                    Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                    var export = Named<Button>(page, "EncryptedBackupExportButton");
                    export.BringIntoView();
                    window.UpdateLayout();
                    Assert.InRange(export.TranslatePoint(new Point(0, export.Bounds.Height), scroll)!.Value.Y, 1, scroll.Viewport.Height + 1);
                }
            }
            backups.Publish(false);
            window.UpdateLayout();
            var stateToggle = Named<CommandToggleSwitch>(host, "AutomaticBackupToggle");
            ClickControl(window, stateToggle);
            Assert.False(stateToggle.IsChecked); // The fake native picker was cancelled.
            Assert.False(model.IsAutomaticBackupConfigured);
            backups.Publish(true);
            window.UpdateLayout();
            ClickControl(window, Named<RadioButton>(host, "LocalBackupLocationChoice"));
            Assert.False(model.UseNetworkBackupFolder);
            ClickControl(window, Named<RadioButton>(host, "NetworkBackupLocationChoice"));
            Assert.True(model.UseNetworkBackupFolder);
            Assert.Equal(1, backups.ConfigureCalls);
        }
        finally { window.Close(); }
    }

    public class UnexpectedBackupService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Layout test unexpectedly invoked {targetMethod?.Name}.");
    }

    private sealed class LayoutBackups : IAutomaticBackupCoordinator
    {
        public event EventHandler? StatusChanged;
        public int ConfigureCalls { get; private set; }
        public AutomaticBackupStatus Status { get; private set; } = AutomaticBackupStatus.NotConfigured;
        public AutomaticBackupDestination? Destination => Status.IsConfigured
            ? new("desktop-folder-v1", @"\\server\shared-backups\OTP Harbor\a longer folder name for recovery copies", "OTP Harbor backups") : null;
        public void Publish(bool configured)
        {
            Status = configured ? new(true, false, "OTP Harbor backups", DateTimeOffset.UnixEpoch,
                AutomaticBackupStatusKind.DestinationUnavailable, AutomaticBackupDestinationAccess.Unavailable,
                AutomaticBackupFailureKind.DestinationUnavailable, DateTimeOffset.UnixEpoch) : AutomaticBackupStatus.NotConfigured;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        public Task<Result> InitializeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Ok());
        public Task<Result<AutomaticBackupRunResult>> ConfigureAsync(CancellationToken cancellationToken = default)
        {
            ConfigureCalls++;
            return Task.FromResult(Result.Ok(new AutomaticBackupRunResult(AutomaticBackupRunStatus.NotConfigured)));
        }
        public Task<Result> DisableAsync(CancellationToken cancellationToken = default) { Publish(false); return Task.FromResult(Result.Ok()); }
        public Task<Result<AutomaticBackupRunResult>> BackupNowAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Fail<AutomaticBackupRunResult>("unavailable"));
        public Task<Result<AutomaticBackupRunResult>> EnsureCurrentCheckpointAsync(CancellationToken cancellationToken = default) => BackupNowAsync(cancellationToken);
        public void RequestBackup() { }
        public void Dispose() { }
    }
}
