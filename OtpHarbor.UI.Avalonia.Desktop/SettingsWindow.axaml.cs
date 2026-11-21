using Avalonia.Controls;

namespace TOTP.Avalonia.Desktop;

public partial class SettingsWindow : global::Avalonia.Controls.Window
{
    private const double PreferredWidth = 900;
    private const double PreferredHeight = 720;
    private const double WorkingAreaMargin = 24;

    public SettingsWindow()
    {
        InitializeComponent();
        FitToScreen(Screens.Primary);
    }

    public void FitToOwnerScreen(global::Avalonia.Controls.Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        FitToScreen(owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary);
    }

    private void FitToScreen(global::Avalonia.Platform.Screen? screen)
    {
        if (screen is null) return;

        var placement = CalculatePlacement(screen.WorkingArea, screen.Scaling);
        MaxWidth = placement.Maximum.Width;
        MaxHeight = placement.Maximum.Height;
        MinWidth = placement.Minimum.Width;
        MinHeight = placement.Minimum.Height;
        Width = placement.Preferred.Width;
        Height = placement.Preferred.Height;
        Position = placement.Position;
    }

    private static (global::Avalonia.Size Preferred, global::Avalonia.Size Minimum,
        global::Avalonia.Size Maximum, global::Avalonia.PixelPoint Position) CalculatePlacement(
        global::Avalonia.PixelRect workArea, double scaling)
    {
        scaling = scaling > 0 ? scaling : 1;
        var maximum = new global::Avalonia.Size(
            Math.Max(1, Math.Floor(workArea.Width / scaling - WorkingAreaMargin)),
            Math.Max(1, Math.Floor(workArea.Height / scaling * 0.8)));
        var minimum = new global::Avalonia.Size(Math.Min(520, maximum.Width), Math.Min(480, maximum.Height));
        var preferred = new global::Avalonia.Size(Math.Min(PreferredWidth, maximum.Width), Math.Min(PreferredHeight, maximum.Height));
        var position = new global::Avalonia.PixelPoint(
            workArea.X + (int)Math.Floor((workArea.Width - preferred.Width * scaling) / 2),
            workArea.Y + (int)Math.Floor((workArea.Height - preferred.Height * scaling) / 2));
        return (preferred, minimum, maximum, position);
    }

    private void FaqSectionPropertyChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Expander.IsExpandedProperty && e.NewValue is true)
            FaqSectionExpanded(sender, new global::Avalonia.Interactivity.RoutedEventArgs());
    }

    private void FaqSectionExpanded(
        object? sender,
        global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not global::Avalonia.Controls.Expander { IsExpanded: true } expanded) return;

        foreach (var section in new[]
                 {
                     FaqMasterPasswordRecoveryExpander,
                     FaqOlderVaultsExpander, FaqIconPacksExpander, FaqImportFormatsExpander,
                     FaqCreateBackupExpander, FaqBackupLocationsExpander, FaqForgottenPasswordExpander
                 })
        {
            if (section is not null && !ReferenceEquals(section, expanded)) section.IsExpanded = false;
        }
    }

    private void PasswordPanelTransitionCompleted(object? sender, global::Avalonia.Controls.TransitionCompletedEventArgs e)
    {
        if (sender is not global::Avalonia.Controls.TransitioningContentControl panel) return;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(panel) is null || !panel.IsEffectivelyVisible || !ReferenceEquals(panel.Content, e.To)) return;
            var descendants = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(panel);
            if (e.To is Presentation.AuthorizationSettingsViewModel.PasswordEditorPresentation)
            {
                var input = descendants.OfType<TOTP.Avalonia.Shared.Controls.RevealableSecretInput>()
                    .FirstOrDefault(value => value.IsEffectivelyVisible);
                input?.BringIntoView();
                input?.FocusInput();
            }
            else if (e.From is Presentation.AuthorizationSettingsViewModel.PasswordEditorPresentation)
            {
                var button = descendants.OfType<global::Avalonia.Controls.Button>()
                    .FirstOrDefault(value => value.Name == "MasterPasswordChangeButton" && value.IsEffectivelyVisible);
                if (button is { IsEffectivelyEnabled: true })
                {
                    button.BringIntoView();
                    button.Focus();
                }
            }
        }, global::Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void FaqImportFormatSectionExpanded(
        object? sender,
        global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not global::Avalonia.Controls.Expander expanded) return;

        foreach (var section in new[]
                 {
                     FaqImportFormatsAegisExpander,
                     FaqImportFormatsTwoFasExpander,
                     FaqImportFormatsOtpAuthExpander
                 })
        {
            if (!ReferenceEquals(section, expanded)) section.IsExpanded = false;
        }

        e.Handled = true;
    }

    private void MoveWindow(
        object? sender,
        global::Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind
            != global::Avalonia.Input.PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        // Keep the custom, decoration-free title area reliably draggable while
        // leaving the close button and every other focusable control interactive.
        if (point.Position.Y > 80 || IsInteractiveSource(e.Source)) return;

        BeginMoveDrag(e);
    }

    private static bool IsInteractiveSource(object? source)
    {
        for (var visual = source as global::Avalonia.Visual;
             visual is not null;
             visual = global::Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual))
        {
            if (visual is global::Avalonia.Controls.Control { Focusable: true }) return true;
            if (visual is SettingsWindow) break;
        }

        return false;
    }
}
