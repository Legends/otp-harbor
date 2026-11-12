using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class ImportFileSummaryTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ConflictWindow_DistinguishesBackFromCancelAndNativeClose(int closeAction)
    {
        var owner = new Window { Width = 1240, Height = 800 };
        var windows = new AvaloniaWindowCoordinator();
        windows.RegisterMainWindow(owner);
        var dialogs = new AvaloniaDialogService(windows, new AvaloniaActivityMonitor(new Heartbeat()));
        var catalog = new AvaloniaStringCatalog();
        var review = new ImportReviewViewModel(new(1, 0, ImportConflictStrategy.SkipExisting)
            { NewCount = 1, Entries = [new("Demo", "alice", "SHA1", 6, 30)] }, null, "2FAS", "export.2fas",
            key => catalog.Get(key, CultureInfo.GetCultureInfo("en")));
        owner.Show();
        try
        {
            var pending = dialogs.ReviewImportAsync(review, TestContext.Current.CancellationToken);
            var dialog = Assert.IsType<ImportReviewWindow>(windows.CurrentActivationTarget);
            if (closeAction == 2) dialog.Close();
            else
            {
                var caption = closeAction == 0 ? review.BackText : review.CancelText;
                var button = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption));
                Assert.Same(closeAction == 0 ? review.BackCommand : review.CancelCommand, button.Command);
                button.Command!.Execute(null);
            }
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(closeAction == 0, review.BackRequested);
            Assert.False(dialog.IsVisible);
            Assert.Same(owner, windows.CurrentActivationTarget);
        }
        finally { windows.CurrentActivationTarget?.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task DesktopSummary_IsOwnedModal_AndClosesBeforeConflictReview(bool review, bool cancel)
    {
        var owner = new Window { Width = 640, Height = 480 };
        var windows = new AvaloniaWindowCoordinator();
        windows.RegisterMainWindow(owner);
        var dialogs = new AvaloniaDialogService(windows, new AvaloniaActivityMonitor(new Heartbeat()));
        var catalog = new AvaloniaStringCatalog();
        string Localize(string key) => catalog.Get(key, CultureInfo.GetCultureInfo("en"));
        var model = new AuthenticatorImportViewModel(Localize, null!, null!);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        owner.Show();
        try
        {
            var pending = dialogs.ConfirmImportFileAsync(model,
                new(6) { Format = AccountImportFormat.TwoFasJson }, "export.2fas", cancellation.Token);
            var summary = Assert.IsType<ImportFileSummaryWindow>(windows.CurrentActivationTarget);
            Assert.Same(owner, summary.Owner);
            Assert.True(summary.IsVisible);
            Assert.True(model.HasSelectedFile);
            Assert.False(pending.IsCompleted);
            if (cancel) cancellation.Cancel();
            else if (review) model.ReviewFileCommand.Execute(null);
            else summary.Close();

            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            else Assert.Equal(review, await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(summary.IsVisible);
            Assert.False(model.HasSelectedFile);
            Assert.Same(owner, windows.CurrentActivationTarget);

            if (review)
            {
                var conflict = new ImportReviewViewModel(new(1, 0, ImportConflictStrategy.SkipExisting)
                { NewCount = 1, Entries = [new("Demo", "alice", "SHA1", 6, 30)] }, null, "2FAS", "export.2fas", Localize);
                var reviewing = dialogs.ReviewImportAsync(conflict, TestContext.Current.CancellationToken);
                Assert.IsType<ImportReviewWindow>(windows.CurrentActivationTarget);
                Assert.False(summary.IsVisible);
                conflict.BackCommand.Execute(null);
                Assert.Null(await reviewing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            model.CancelPendingFile();
            windows.CurrentActivationTarget?.Close();
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(360, 480, true, "de")]
    [InlineData(430, 640, false, "fr")]
    [InlineData(720, 420, true, "es")]
    public async Task SummaryOverlay_KeepsReviewReachableAtConstrainedSizes(int width, int height, bool dark, string language)
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new AuthenticatorImportViewModel(key => catalog.Get(key, CultureInfo.GetCultureInfo(language)), null!, null!);
        var pending = model.WaitForFileReviewAsync(new(23, nonOtpCount: 1) { Format = AccountImportFormat.AegisJson },
            "an-export-with-a-long-name-that-must-wrap-without-hiding-the-actions.json", TestContext.Current.CancellationToken);
        var panel = new ImportFileSummaryPanel { DataContext = model };
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new Border { Margin = new Thickness(16), MaxWidth = 600, MaxHeight = 640,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Child = panel } };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(24, model.TotalEntries);
            foreach (var command in new[] { model.ReviewFileCommand, model.ClearFileCommand })
            {
                var button = panel.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
                var point = button.TranslatePoint(default, window)!.Value;
                Assert.True(button.Bounds.Height >= 44);
                Assert.True(point.X >= 0 && point.X + button.Bounds.Width <= width + 1);
                Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= height + 1);
            }
            model.ClearFileCommand.Execute(null);
            Assert.False(await pending);
        }
        finally { model.CancelPendingFile(); window.Close(); }
    }

    private sealed class Heartbeat : IActivityHeartbeat
    {
        public void RecordActivity() { }
    }
}
