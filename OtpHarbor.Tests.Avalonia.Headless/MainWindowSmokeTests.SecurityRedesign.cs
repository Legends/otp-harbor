using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class MainWindowSmokeTests
{
    [AvaloniaTheory]
    [InlineData(0, 0, 1920, 1040, 1)]
    [InlineData(1920, 32, 2560, 1408, 1.25)]
    [InlineData(-1920, 40, 1920, 1040, 1.5)]
    [InlineData(0, -1080, 1920, 1040, 2)]
    [InlineData(0, 0, 1366, 728, 1.25)]
    public void SettingsPlacement_RespectsWorkingAreaHeightAndCenterAtCommonScales(int x, int y, int width, int height, double scale)
    {
        var method = typeof(SettingsWindow).GetMethod("CalculatePlacement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var placement = ((Size Preferred, Size Minimum, Size Maximum, PixelPoint Position))method.Invoke(null, [new PixelRect(x, y, width, height), scale])!;
        Assert.InRange(placement.Preferred.Height, 1, height / scale * .8);
        Assert.InRange(placement.Maximum.Height, placement.Preferred.Height, height / scale * .8);
        Assert.True(placement.Minimum.Height <= placement.Maximum.Height);
        Assert.True(placement.Minimum.Width <= placement.Maximum.Width);
        Assert.InRange(placement.Position.X, x, x + width);
        Assert.InRange(placement.Position.Y, y, y + height);
        Assert.True(placement.Position.X + placement.Preferred.Width * scale <= x + width);
        Assert.True(placement.Position.Y + placement.Preferred.Height * scale <= y + height);
        Assert.InRange(Math.Abs(placement.Position.Y + placement.Preferred.Height * scale / 2 - (y + height / 2.0)), 0, 1);
    }

    [AvaloniaFact]
    public void SettingsTabs_HideVerticalBarsAndKeepContentScrollableWithBodyTypography()
    {
        var window = new SettingsWindow { Width = 520, Height = 416, MinHeight = 416 };
        try
        {
            window.Show();
            var tabs = window.FindControl<TabControl>("SettingsCategories")!;
            var overflow = false;
            for (var index = 0; index < 7; index++)
            {
                tabs.SelectedIndex = index;
                window.UpdateLayout();
                var tab = (TabItem)tabs.Items[index]!;
                var label = ((Grid)tab.Header!).Children.OfType<TextBlock>().Single();
                Assert.Equal(window.FontSize, label.FontSize);
                var content = (Control)tab.Content!;
                if (content is ContentControl { Content: null, ContentTemplate: { } template } host)
                {
                    var body = (Control)template.Build(null)!;
                    host.ContentTemplate = null;
                    host.Content = body;
                    window.UpdateLayout();
                }
                var scrollers = new Control[] { content }.Concat(content.GetVisualDescendants().OfType<Control>()).OfType<ScrollViewer>()
                    .Where(control => !control.GetVisualAncestors().Any(ancestor => ancestor is TextBox or ComboBox)).ToArray();
                Assert.NotEmpty(scrollers);
                foreach (var scroller in scrollers)
                {
                    Assert.Equal(ScrollBarVisibility.Hidden, scroller.VerticalScrollBarVisibility);
                    if (scroller.Extent.Height <= scroller.Viewport.Height + 1) continue;
                    scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
                    window.UpdateLayout();
                    Assert.True(scroller.Offset.Y > 0);
                    overflow = true;
                }
            }
            Assert.True(overflow);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(520, 416)]
    [InlineData(960, 820)]
    public async Task SecurityCards_InlineEditorReflowsAndRetainsBindingsAndMaskedInputs(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(480, height) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        var preferences = new TestSettingsService();
        using var settings = new SettingsPageViewModel(preferences, localization);
        var service = new LayoutAuthorization();
        using var authorization = new AuthorizationSettingsViewModel(service, new LayoutDialogs(), localization,
            new PasswordValidationService(), settingsService: preferences, unattendedUnlock: new LayoutUnattended());
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 2;
            window.FindControl<ContentControl>("AuthorizationSettingsHost")!.Content = authorization;
            window.FindControl<ContentControl>("SecurityBehaviorSettingsHost")!.Content = settings;
            await authorization.RefreshAsync();
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                window.RequestedThemeVariant = theme;
                window.UpdateLayout();
                var panel = window.FindControl<StackPanel>("SecuritySettingsPanel")!;
                var scroller = window.FindControl<ScrollViewer>("SecuritySettingsScroll")!;
                var toggle = Named<CommandToggleSwitch>(panel, "AppLockToggleButton");
                var bio = Named<CommandRadioButton>(panel, "BiometricUnlockChoice");
                var password = Named<CommandRadioButton>(panel, "PasswordUnlockChoice");
                Assert.Same(authorization.ToggleAppLockCommand, toggle.Command);
                Assert.Same(authorization.EnableQuickUnlockCommand, bio.Command);
                Assert.Same(authorization.UsePasswordCommand, password.Command);
                Assert.True(toggle.IsChecked);
                Assert.True(password.IsChecked);
                Assert.False(bio.IsChecked);
                AssertSurface(Named<Border>(password, "ChoiceSurface"), "BrushAccountItemSelectedBackground", window, theme);
                var compact = panel.Bounds.Width <= 520;
                Assert.Equal(compact ? 1 : 0, Grid.GetRow(password));
                Assert.Equal(compact ? 0 : 1, Grid.GetColumn(password));
                if (!compact) Assert.Equal(bio.Bounds.Height, password.Bounds.Height, 1);
                var master = Named<Border>(panel, "MasterPasswordSettingsPanel");
                var open = Named<Button>(master, "MasterPasswordChangeButton");
                Assert.DoesNotContain(master.GetVisualDescendants().OfType<StackPanel>(), control => control.Name == "MasterPasswordEditor" && control.IsEffectivelyVisible);
                var transition = Named<TransitioningContentControl>(master, "PasswordPanelTransition");
                await ClickPasswordAction<AuthorizationSettingsViewModel.PasswordEditorPresentation>(window, open, transition);
                var editor = Named<StackPanel>(master, "MasterPasswordEditor");
                Assert.True(authorization.IsPasswordEditorOpen);
                Assert.True(editor.IsVisible);
                Assert.DoesNotContain(open, master.GetVisualDescendants());
                Assert.Contains("editing", master.Classes);
                AssertSurface(master, "BrushSurfaceAlternate", window, theme);
                Assert.True(window.TryFindResource("BrushAccent", theme, out var accent));
                Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(accent).Color, Assert.IsAssignableFrom<ISolidColorBrush>(master.BorderBrush).Color);
                var inputs = editor.GetVisualDescendants().OfType<RevealableSecretInput>().ToArray();
                Assert.Equal(3, inputs.Length);
                Assert.True(inputs[0].GetVisualDescendants().OfType<TextBox>().Single().IsFocused);
                foreach (var input in inputs)
                {
                    Assert.False(input.IsRevealed);
                    Assert.False(string.IsNullOrWhiteSpace(input.AccessibleName));
                    input.Text = "test-password";
                    input.BringIntoView();
                    window.UpdateLayout();
                    var left = input.TranslatePoint(default, panel)!.Value.X;
                    Assert.True(input.Bounds.Width > 0);
                    Assert.InRange(left + input.Bounds.Width, 0, panel.Bounds.Width + 1);
                }
                Assert.Equal("test-password", authorization.CurrentPassword);
                Assert.Equal("test-password", authorization.NewPassword);
                Assert.Equal("test-password", authorization.ConfirmPassword);
                Assert.Equal(language switch { "de" => "Speichern", "fr" => "Enregistrer", "es" => "Guardar", _ => "Save" },
                    Named<Button>(editor, "SaveMasterPasswordButton").Content);
                Assert.Equal(8, Named<ProgressBar>(editor, "PasswordLengthIndicator").Value);
                Assert.Equal(authorization.PasswordMinimumLengthHelp, Named<TextBlock>(editor, "PasswordMinimumLengthHelp").Text);
                AssertButtonTextCentered(window, Named<Button>(editor, "SaveMasterPasswordButton"));
                AssertButtonTextCentered(window, Named<Button>(editor, "CancelMasterPasswordButton"));
                await ClickPasswordAction<AuthorizationSettingsViewModel.PasswordSummaryPresentation>(window, Named<Button>(editor, "CancelMasterPasswordButton"), transition);
                Assert.DoesNotContain(editor, master.GetVisualDescendants());
                open = Named<Button>(master, "MasterPasswordChangeButton");
                Assert.True(open.IsEffectivelyVisible);
                Assert.True(open.IsFocused);
                Assert.Equal(localization.GetString(AvaloniaStringKeys.SecurityChangePasswordAction), open.Content);
                Assert.All(inputs, input => Assert.Empty(input.Text));
                await ClickPasswordAction<AuthorizationSettingsViewModel.PasswordEditorPresentation>(window, open, transition);
                editor = Named<StackPanel>(master, "MasterPasswordEditor");
                inputs = editor.GetVisualDescendants().OfType<RevealableSecretInput>().ToArray();
                inputs[0].Text = "current-password";
                inputs[1].Text = "new-password";
                inputs[2].Text = "new-password";
                await ClickPasswordAction<AuthorizationSettingsViewModel.PasswordSummaryPresentation>(window, Named<Button>(editor, "SaveMasterPasswordButton"), transition);
                Assert.False(authorization.IsPasswordEditorOpen);
                Assert.Equal(("current-password", "new-password"), service.RotatedPasswords);
                Assert.All(inputs, input => Assert.Empty(input.Text));
                Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                foreach (var name in new[] { "AutoLockSelector", "ClipboardLifetimeSelector" })
                {
                    var choice = Named<ComboBox>(panel, name);
                    choice.BringIntoView();
                    window.UpdateLayout();
                    var left = choice.TranslatePoint(default, panel)!.Value.X;
                    Assert.InRange(left + choice.Bounds.Width, 0, panel.Bounds.Width + 1);
                    Assert.Equal(compact ? 1 : 0, Grid.GetRow(choice));
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 416)]
    [InlineData(960, 820)]
    public void AutoLockNotice_IsLocalizedAndVisibleOnlyWhileAppLockIsDisabled(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height, MinHeight = Math.Min(480, height) };
        var localization = new AvaloniaLocalizationService(window.Resources, new AvaloniaStringCatalog());
        using var settings = new SettingsPageViewModel(new TestSettingsService(), localization);
        try
        {
            window.FindControl<Border>("SettingsImportProgressOverlay")!.IsVisible = false;
            window.Show();
            window.FindControl<TabControl>("SettingsCategories")!.SelectedIndex = 2;
            window.FindControl<ContentControl>("SecurityBehaviorSettingsHost")!.Content = settings;
            foreach (var language in new[] { "en", "de", "fr", "es" })
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                localization.ApplyCulture(language);
                window.RequestedThemeVariant = theme;
                window.UpdateLayout();
                var card = Named<Border>(window, "SecurityBehaviorSettings");
                var notice = Named<TextBlock>(card, "AutoLockRequiresAppLockNotice");
                foreach (var enabled in new[] { false, true })
                {
                    card.IsEnabled = enabled;
                    window.UpdateLayout();
                    Assert.Equal(!enabled, notice.IsVisible);
                    Assert.Equal(localization.GetString(AvaloniaStringKeys.SecurityAutoLockRequiresAppLock), notice.Text);
                    Assert.Equal(enabled, Named<ComboBox>(card, "AutoLockSelector").IsEffectivelyEnabled);
                    if (!enabled)
                    {
                        Assert.True(notice.Bounds.Width > 0);
                        var position = notice.TranslatePoint(default, card)!.Value;
                        Assert.InRange(position.X, 0, card.Bounds.Width);
                        Assert.InRange(position.X + notice.Bounds.Width, 0, card.Bounds.Width + 1);
                    }
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CommandSelections_WaitForConfirmedStateForPointerKeyboardAndAutomation()
    {
        var calls = 0;
        var command = new SecurityAction(() => calls++);
        var toggle = new CommandToggleSwitch { IsChecked = true, Command = command };
        var radio = new CommandRadioButton { IsChecked = false, Command = command };
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { toggle, radio } } };
        try
        {
            window.Show(); window.UpdateLayout();
            ClickControl(window, toggle);
            Assert.True(toggle.IsChecked);
            Assert.Equal(1, calls);
            radio.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(radio.IsChecked);
            Assert.Equal(2, calls);
            var peer = ControlAutomationPeer.CreatePeerForElement(toggle)!;
            Assert.IsAssignableFrom<IToggleProvider>(peer).Toggle();
            Assert.True(toggle.IsChecked);
            Assert.Equal(3, calls);
            Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(radio)!).Select();
            Assert.False(radio.IsChecked);
            Assert.Equal(4, calls);
            toggle.IsChecked = false;
            radio.IsChecked = true;
            Assert.False(toggle.IsChecked);
            Assert.True(radio.IsChecked);
        }
        finally { window.Close(); }
    }

    private static async Task ClickPasswordAction<T>(Window window, Button button, TransitioningContentControl transition)
    {
        var completed = new TaskCompletionSource<TransitionCompletedEventArgs>();
        void Finished(object? sender, TransitionCompletedEventArgs args)
        {
            if (args.To is T && ReferenceEquals(transition.Content, args.To)) completed.TrySetResult(args);
        }
        transition.TransitionCompleted += Finished;
        try
        {
            ClickControl(window, button);
            window.UpdateLayout();
            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(result.HasRunToCompletion);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        finally { transition.TransitionCompleted -= Finished; }
    }

    private static void AssertButtonTextCentered(Window window, Button button)
    {
        foreach (var state in new[] { "normal", "hover", "focus", "disabled" })
        {
            button.ClearValue(InputElement.IsEnabledProperty);
            window.MouseMove(new Point(-1, -1));
            if (state == "hover") MovePointerTo(window, button);
            if (state == "focus") button.Focus();
            if (state == "disabled") button.IsEnabled = false;
            window.UpdateLayout();
            var text = button.GetVisualDescendants().OfType<TextBlock>().Single(value => value.Text == button.Content?.ToString());
            var center = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), button)!.Value;
            Assert.InRange(Math.Abs(center.Y - button.Bounds.Height / 2), 0, 1);
            Assert.InRange(Math.Abs(center.X - button.Bounds.Width / 2), 0, 1);
        }
        button.ClearValue(InputElement.IsEnabledProperty);
        window.UpdateLayout();
    }

    private sealed class SecurityAction(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
