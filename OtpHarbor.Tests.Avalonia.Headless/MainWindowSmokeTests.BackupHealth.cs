using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
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
    [InlineData(1440, 820, "en", true)]
    [InlineData(1200, 720, "de", false)]
    [InlineData(900, 720, "fr", true)]
    [InlineData(520, 416, "es", false)]
    public void BackupHealthCard_ReflowsAndReflectsActualHealthWithAccessibleDisclosure(int width, int height, string culture, bool dark)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(480, height) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        localization.ApplyCulture(culture);
        var backups = new LayoutBackups();
        using var model = new NativeFilePickerViewModel(
            DispatchProxy.Create<IAvaloniaFilePicker, UnexpectedBackupService>(),
            DispatchProxy.Create<IExportService, UnexpectedBackupService>(),
            DispatchProxy.Create<IAccountManager, UnexpectedBackupService>(),
            DispatchProxy.Create<IAccountImportService, UnexpectedBackupService>(),
            new LayoutDialogs(), new PasswordValidationService(),
            DispatchProxy.Create<IPlatformFileSecurity, UnexpectedBackupService>(), new TestSettingsService(),
            DispatchProxy.Create<IPlatformFolderLauncher, UnexpectedBackupService>(), localization, automaticBackup: backups);
        var lastSuccess = new DateTimeOffset(new DateTime(2026, 11, 27, 18, 41, 0, DateTimeKind.Local));
        var healthy = new AutomaticBackupStatus(true, false, "Synthetic backup folder", lastSuccess,
            AutomaticBackupStatusKind.UpToDate, AutomaticBackupDestinationAccess.Accessible);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 3;
            var host = window.FindControl<ContentControl>("ImportExportSettingsHost")!;
            host.Content = model;
            backups.Publish(healthy);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var card = Named<Expander>(host, "AutomaticBackupHealthDetails");
            card.BringIntoView();
            card.ApplyTemplate();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var header = Named<ToggleButton>(card, "BackupHealthHeader");
            Assert.Single(header.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>(),
                path => !path.GetVisualAncestors().OfType<SymbolIcon>().Any());
            var status = Named<TextBlock>(card, "AutomaticBackupHealthStatus");
            Assert.False(card.IsExpanded);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.AutomaticBackupHealthUpToDate), status.Text);
            Assert.True(model.IsAutomaticBackupHealthy);
            Assert.Equal(HorizontalAlignment.Stretch, card.HorizontalAlignment);
            Assert.InRange(Math.Abs(header.Bounds.Width - card.Bounds.Width), 0, 2);
            Assert.True(header.Bounds.Height >= 48);
            PreviewBackupHealth(card, culture + "-collapsed");
            header.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsExpanded);
            var values = Named<Grid>(card, "AutomaticBackupHealthValues");
            var action = Named<Button>(card, "AutomaticBackupNowButton");
            Assert.True(values.IsEffectivelyVisible);
            Assert.Equal(model.AutomaticBackupLastSuccessValue, Named<TextBlock>(card, "AutomaticBackupLastSuccessValue").Text);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.AutomaticBackupAccessAvailable), Named<TextBlock>(card, "AutomaticBackupAccessValue").Text);
            Assert.Same(model.AutomaticBackupNowCommand, action.Command);
            Assert.Equal(card.Bounds.Width <= 560 ? 2 : 0, Grid.GetRow(action));
            Assert.Equal(card.Bounds.Width <= 560 ? 0 : 1, Grid.GetColumn(action));
            Assert.Equal(card.Bounds.Width <= 560 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right, action.HorizontalAlignment);
            Assert.Single(header.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>(),
                path => !path.GetVisualAncestors().OfType<SymbolIcon>().Any());
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(Named<Border>(header, "BackupHealthHeaderBackground").Background).Color.A);
            Assert.Equal(VerticalAlignment.Center, action.VerticalContentAlignment);
            Assert.True(action.Bounds.Height >= 44);
            foreach (var label in values.GetVisualDescendants().OfType<TextBlock>())
            {
                Assert.True(label.Bounds.Height >= label.TextLayout.Height - 1, label.Text);
                Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1, label.Text);
            }
            var position = action.TranslatePoint(default, card)!.Value;
            Assert.True(position.X >= 0 && position.X + action.Bounds.Width <= card.Bounds.Width + 1);
            Assert.True(position.Y + action.Bounds.Height <= card.Bounds.Height + 1);
            PreviewBackupHealth(card, culture + "-expanded");

            backups.Publish(healthy with { Kind = AutomaticBackupStatusKind.BackingUp, IsBusy = true });
            window.UpdateLayout();
            Assert.False(model.IsAutomaticBackupHealthy);
            Assert.Equal(model.AutomaticBackupNowCommand.CanExecute(null), action.IsEffectivelyEnabled);
            Assert.Equal(model.AutomaticBackupStatusText, status.Text);

            backups.Publish(healthy with { Kind = AutomaticBackupStatusKind.DestinationUnavailable,
                DestinationAccess = AutomaticBackupDestinationAccess.Unavailable,
                LastFailureKind = AutomaticBackupFailureKind.DestinationUnavailable, LastFailureUtc = lastSuccess });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsExpanded);
            Assert.False(model.IsAutomaticBackupHealthy);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.RetryAutomaticBackup), model.AutomaticBackupActionText);
            var failure = Named<TextBlock>(card, "AutomaticBackupHealthFailure");
            Assert.True(failure.IsEffectivelyVisible);
            Assert.Equal(model.AutomaticBackupLastFailureText, failure.Text);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.AutomaticBackupAccessUnavailable), Named<TextBlock>(card, "AutomaticBackupAccessValue").Text);
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(failure.Foreground).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(Named<Ellipse>(card, "AutomaticBackupAccessIndicator").Fill).Color);
            var nextCulture = culture == "de" ? "fr" : "de";
            localization.ApplyCulture(nextCulture);
            window.UpdateLayout();
            Assert.Equal(localization.GetString(AvaloniaStringKeys.AutomaticBackupAccessUnavailable), Named<TextBlock>(card, "AutomaticBackupAccessValue").Text);
            Assert.Equal(model.AutomaticBackupHealthTitle, status.Text);
            Assert.Equal(model.AutomaticBackupLastFailureText, failure.Text);
            PreviewBackupHealth(card, culture + "-failure");

            backups.Publish(healthy with { Kind = AutomaticBackupStatusKind.Ready, LastSuccessfulUtc = null,
                DestinationAccess = AutomaticBackupDestinationAccess.Unknown });
            window.UpdateLayout();
            Assert.False(model.IsAutomaticBackupHealthy);
            Assert.True(model.IsAutomaticBackupHealthPending);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.AutomaticBackupNever), model.AutomaticBackupLastSuccessValue);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.AutomaticBackupAccessUnknown), model.AutomaticBackupDestinationAccessValue);
            backups.Publish(healthy with { Kind = AutomaticBackupStatusKind.Succeeded });
            Assert.True(model.IsAutomaticBackupHealthy);
            Assert.False(model.HasAutomaticBackupFailure);
            Assert.Equal(model.AutomaticBackupStatusText, model.AutomaticBackupHealthTitle);
        }
        finally { window.Close(); }
    }

    private static void PreviewBackupHealth(Expander card, string name)
    {
        if (Environment.GetEnvironmentVariable("OTP_HARBOR_TEST_PREVIEW_DIR") is not { Length: > 0 } preview) return;
        Directory.CreateDirectory(preview);
        using var frame = RenderCardSurface(card, 2);
        frame.Save(System.IO.Path.Combine(preview, $"backup-health-{name}.png"), new PngBitmapEncoderOptions());
    }
}
