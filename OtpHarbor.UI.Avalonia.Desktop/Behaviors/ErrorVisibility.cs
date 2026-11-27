using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Behaviors;

public sealed class ErrorVisibility : AvaloniaObject
{
    public static readonly AttachedProperty<bool> RevealOnErrorProperty =
        AvaloniaProperty.RegisterAttached<ErrorVisibility, Control, bool>("RevealOnError");
    private static readonly AttachedProperty<bool> RevealPendingProperty =
        AvaloniaProperty.RegisterAttached<ErrorVisibility, Control, bool>("RevealPending");

    static ErrorVisibility()
    {
        RevealOnErrorProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            control.PropertyChanged -= FeedbackChanged;
            control.AttachedToVisualTree -= FeedbackAttached;
            if (!GetRevealOnError(control)) return;
            control.PropertyChanged += FeedbackChanged;
            control.AttachedToVisualTree += FeedbackAttached;
            ScheduleReveal(control);
        });
    }

    public static bool GetRevealOnError(Control control) => control.GetValue(RevealOnErrorProperty);
    public static void SetRevealOnError(Control control, bool value) => control.SetValue(RevealOnErrorProperty, value);

    private static void FeedbackAttached(object? sender, VisualTreeAttachmentEventArgs args) => ScheduleReveal((Control)sender!);

    private static void FeedbackChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ValidationMessage.TextProperty || args.Property == ValidationMessage.SeverityProperty
            || args.Property == NotificationBanner.TextProperty || args.Property == NotificationBanner.SeverityProperty
            || args.Property == Visual.IsVisibleProperty)
            ScheduleReveal((Control)sender!);
    }

    private static bool HasError(Control control) => control switch
    {
        ValidationMessage message => message.Severity == ValidationSeverity.Error && !string.IsNullOrWhiteSpace(message.Text),
        NotificationBanner banner => banner.Severity == NotificationSeverity.Error && !string.IsNullOrWhiteSpace(banner.Text),
        _ => false
    };

    private static void ScheduleReveal(Control control)
    {
        if (!GetRevealOnError(control) || !HasError(control) || !control.IsEffectivelyVisible
            || TopLevel.GetTopLevel(control) is null || control.GetValue(RevealPendingProperty)) return;
        control.SetValue(RevealPendingProperty, true);
        Dispatcher.UIThread.Post(() =>
        {
            control.SetValue(RevealPendingProperty, false);
            if (!GetRevealOnError(control) || !HasError(control) || !control.IsEffectivelyVisible) return;
            var scroller = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is null) return;
            var firstError = scroller.GetVisualDescendants().OfType<Control>().FirstOrDefault(candidate =>
                GetRevealOnError(candidate) && candidate.IsEffectivelyVisible && HasError(candidate));
            // A save can validate several fields together; reveal the first error once.
            if (firstError == control) control.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
