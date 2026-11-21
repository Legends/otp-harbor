using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaFact]
    public async Task PasswordPanel_DetachingDuringCollapseReleasesLayoutAndCanBeReused()
    {
        var expanded = new Border { Height = 300 };
        var summary = new Border { Height = 72 };
        var panel = new CollapsingContentControl
        {
            ClipToBounds = true,
            PageTransition = new CollapseThenFadeTransition(),
            Content = expanded
        };
        var stack = new StackPanel { Children = { panel } };
        var window = new Window { Width = 520, Height = 480, Content = stack };
        try
        {
            window.Show();
            window.UpdateLayout();
            await Task.Delay(250, TestContext.Current.CancellationToken);
            Assert.Equal(300, panel.Bounds.Height, 1);
            var completed = new TaskCompletionSource<TransitionCompletedEventArgs>();
            panel.TransitionCompleted += (_, args) =>
            {
                if (ReferenceEquals(args.To, panel.Content)) completed.TrySetResult(args);
            };
            var detached = false;
            void DetachDuringCollapse(object? sender, EventArgs args)
            {
                if (detached || panel.Bounds.Height >= 299 || panel.Bounds.Height <= 73) return;
                detached = true;
                window.Content = null;
            }
            window.LayoutUpdated += DetachDuringCollapse;
            panel.Content = summary;
            window.UpdateLayout();
            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            window.LayoutUpdated -= DetachDuringCollapse;
            Assert.True(detached, $"Panel final height {panel.Bounds.Height}, explicit {panel.Height}; visible {summary.IsEffectivelyVisible}.");
            Assert.False(result.HasRunToCompletion);
            Assert.True(double.IsNaN(panel.Height));
            Assert.True(panel.IsEnabled);

            window.Content = stack;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(72, panel.Bounds.Height, 1);
            Assert.True(summary.IsEffectivelyVisible);
            completed = new TaskCompletionSource<TransitionCompletedEventArgs>();
            panel.Content = expanded;
            window.UpdateLayout();
            Assert.True((await completed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)).HasRunToCompletion);
            Assert.True(double.IsNaN(panel.Height));
            window.UpdateLayout();
            Assert.Equal(300, panel.Bounds.Height, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 416, false)]
    [InlineData(520, 416, true)]
    [InlineData(960, 720, false)]
    [InlineData(960, 720, true)]
    public async Task PasswordEditor_CloseShrinksBeforeFadingAndKeepsFollowingSectionInFlow(int width, int height, bool save)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(480, height), RequestedThemeVariant = ThemeVariant.Dark };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        localization.ApplyCulture(width == 520 ? "fr" : "en");
        var preferences = new TestSettingsService();
        using var settings = new SettingsPageViewModel(preferences, localization);
        using var authorization = new AuthorizationSettingsViewModel(new LayoutAuthorization(), new LayoutDialogs(), localization,
            new PasswordValidationService(), settingsService: preferences, unattendedUnlock: new LayoutUnattended());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 2;
            window.FindControl<ContentControl>("AuthorizationSettingsHost")!.Content = authorization;
            window.FindControl<ContentControl>("SecurityBehaviorSettingsHost")!.Content = settings;
            await authorization.RefreshAsync();
            window.UpdateLayout();
            var page = window.FindControl<StackPanel>("SecuritySettingsPanel")!;
            var card = Named<Border>(page, "MasterPasswordSettingsPanel");
            var next = Named<Border>(page, "SecurityBehaviorSettings");
            var summaryHeight = card.Bounds.Height;
            var summaryNextTop = next.TranslatePoint(default, page)!.Value.Y;
            var transition = Named<TransitioningContentControl>(card, "PasswordPanelTransition");
            await ClickPasswordAction<AuthorizationSettingsViewModel.PasswordEditorPresentation>(window,
                Named<Button>(card, "MasterPasswordChangeButton"), transition);
            var expandedHeight = card.Bounds.Height;
            var editor = Named<StackPanel>(card, "MasterPasswordEditor");
            var outgoing = editor.GetVisualAncestors().OfType<ContentPresenter>().Single(control => ReferenceEquals(control.TemplatedParent, transition));
            var inputs = editor.GetVisualDescendants().OfType<RevealableSecretInput>().ToArray();
            inputs[0].Text = "current-password";
            inputs[1].Text = "new-password";
            inputs[2].Text = "new-password";
            var completed = new TaskCompletionSource<TransitionCompletedEventArgs>();
            transition.TransitionCompleted += (_, args) =>
            {
                if (args.To is AuthorizationSettingsViewModel.PasswordSummaryPresentation) completed.TrySetResult(args);
            };
            var samples = new List<(double Height, double Next, double EditorOpacity, bool SummaryVisible)>();
            void Capture(object? sender, EventArgs args)
            {
                if (authorization.IsPasswordEditorOpen || completed.Task.IsCompleted) return;
                var summary = transition.GetVisualDescendants().OfType<Grid>().SingleOrDefault(control => control.Name == "MasterPasswordSummaryRow");
                if (summary is null) return;
                var incoming = summary.GetVisualAncestors().OfType<ContentPresenter>().Single(control => ReferenceEquals(control.TemplatedParent, transition));
                samples.Add((card.Bounds.Height, next.TranslatePoint(default, page)!.Value.Y, outgoing.Opacity,
                    incoming.IsVisible && incoming.Opacity > .01));
            }
            window.LayoutUpdated += Capture;
            ClickControl(window, Named<Button>(editor, save ? "SaveMasterPasswordButton" : "CancelMasterPasswordButton"));
            Assert.False(authorization.IsPasswordEditorOpen);
            Assert.Empty(authorization.CurrentPassword);
            Assert.Empty(authorization.NewPassword);
            Assert.Empty(authorization.ConfirmPassword);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!completed.Task.IsCompleted)
            {
                window.UpdateLayout();
                Capture(null, EventArgs.Empty);
                await Task.Delay(16, timeout.Token);
            }
            window.LayoutUpdated -= Capture;
            Assert.True((await completed.Task).HasRunToCompletion);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(samples, sample => sample.Height > summaryHeight + 1 && sample.Height < expandedHeight - 1);
            Assert.All(samples.Where(sample => sample.Height > summaryHeight + 1), sample =>
            {
                Assert.False(sample.SummaryVisible);
                Assert.InRange(sample.EditorOpacity, .99, 1);
            });
            Assert.All(samples.Where(sample => sample.SummaryVisible), sample => Assert.InRange(Math.Abs(sample.Height - summaryHeight), 0, 1));
            foreach (var pair in samples.Zip(samples.Skip(1)))
            {
                Assert.True(pair.Second.Height <= pair.First.Height + 1);
                Assert.InRange(Math.Abs((pair.Second.Next - pair.First.Next) - (pair.Second.Height - pair.First.Height)), 0, 1);
            }
            Assert.Equal(summaryHeight, card.Bounds.Height, 1);
            Assert.Equal(summaryNextTop, next.TranslatePoint(default, page)!.Value.Y, 1);
            Assert.InRange(Math.Abs(samples[^1].Height - card.Bounds.Height), 0, 1);
            Assert.True(double.IsNaN(transition.Height));
            Assert.True(transition.IsEnabled);
            window.Width = width == 520 ? 960 : 520;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(width == 520 ? card.Bounds.Height < summaryHeight : card.Bounds.Height > summaryHeight,
                $"Summary height {summaryHeight} -> {card.Bounds.Height}; window {window.ClientSize.Width}, page {page.Bounds.Width}.");
        }
        finally { window.Close(); }
    }
}
