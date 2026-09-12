using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Mobile.Views;

public partial class MainView : UserControl
{
    private const string UnlockMethodAttentionClass = "unlock-method-attention";
    private Control? _openSwipeRow;
    private Control? _swipedRow;

    public MainView()
    {
        InitializeComponent();
    }

    private void HighlightUnlockMethodConfirmation(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not MobileShellViewModel
                {
                    IsBiometricEnrollmentVisible: true
                })
            {
                return;
            }

            UnlockMethodPasswordBox.Classes.Remove(UnlockMethodAttentionClass);
            UnlockMethodPasswordBox.Classes.Add(UnlockMethodAttentionClass);
            UnlockMethodPasswordBox.Focus();

            DispatcherTimer.RunOnce(
                () => UnlockMethodPasswordBox.Classes.Remove(UnlockMethodAttentionClass),
                TimeSpan.FromMilliseconds(1200));
        }, DispatcherPriority.Background);
    }

    private void CopyAccountCode(object? sender, TappedEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: MobileAccountItem account,
                RenderTransform: TranslateTransform transform
            } control
            || DataContext is not MobileShellViewModel viewModel)
        {
            return;
        }

        if (ReferenceEquals(_swipedRow, control) || Math.Abs(transform.X) > 0)
        {
            ResetSwipe(control);
            e.Handled = true;
            return;
        }

        _ = viewModel.CopyAccountCodeAsync(account);
        e.Handled = true;
    }

    private void TrackAccountSwipe(object? sender, SwipeGestureEventArgs e)
    {
        if (sender is not Control { RenderTransform: TranslateTransform transform } control)
        {
            return;
        }

        if (_openSwipeRow is not null && !ReferenceEquals(_openSwipeRow, control))
        {
            ResetSwipe(_openSwipeRow);
        }

        _swipedRow = control;
        transform.X = MobileAccountSwipeBehavior.ApplyAvaloniaDelta(
            transform.X,
            e.Delta.X);
        e.Handled = true;
    }

    private void CompleteAccountSwipe(object? sender, SwipeGestureEndedEventArgs e)
    {
        if (sender is not Control
            {
                DataContext: MobileAccountItem account,
                RenderTransform: TranslateTransform transform
            } control)
        {
            return;
        }

        var offset = transform.X;
        var completion = MobileAccountSwipeBehavior.Complete(offset);
        if (completion == MobileAccountSwipeCompletion.ConfirmDelete)
        {
            transform.X = 0;
            _openSwipeRow = null;
            if (DataContext is MobileShellViewModel viewModel)
                _ = viewModel.BeginDeleteForAccountAsync(account);
        }
        else
        {
            transform.X = completion == MobileAccountSwipeCompletion.RevealQrAndEdit
                ? MobileAccountSwipeBehavior.QrAndEditRevealOffset
                : 0d;
            _openSwipeRow = transform.X == 0 ? null : control;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_swipedRow, control)) _swipedRow = null;
        });
        e.Handled = true;
    }

    private void ShowQrForAccount(object? sender, RoutedEventArgs e)
    {
        if (TryGetAccountAction(sender, out var viewModel, out var account))
            _ = viewModel.ShowQrForAccountAsync(account);
        e.Handled = true;
    }

    private void EditAccount(object? sender, RoutedEventArgs e)
    {
        if (TryGetAccountAction(sender, out var viewModel, out var account))
            _ = viewModel.BeginEditForAccountAsync(account);
        e.Handled = true;
    }

    private bool TryGetAccountAction(
        object? sender,
        out MobileShellViewModel viewModel,
        out MobileAccountItem account)
    {
        ResetSwipe(_openSwipeRow);
        if (sender is Control { DataContext: MobileAccountItem item }
            && DataContext is MobileShellViewModel shell)
        {
            viewModel = shell;
            account = item;
            return true;
        }

        viewModel = null!;
        account = null!;
        return false;
    }

    private void ResetSwipe(Control? control)
    {
        if (control?.RenderTransform is TranslateTransform transform) transform.X = 0;
        if (ReferenceEquals(_openSwipeRow, control)) _openSwipeRow = null;
    }
}
