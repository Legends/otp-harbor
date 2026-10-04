using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Mobile.Views;

public partial class MainView : UserControl
{
    private const string UnlockMethodAttentionClass = "unlock-method-attention";
    private static readonly TimeSpan AccountListScrollIdleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AccountListScrollStateCheckInterval = TimeSpan.FromMilliseconds(50);
    private Control? _openSwipeRow;
    private Control? _swipedRow;
    private ScrollViewer? _accountListScrollViewer;
    private readonly DispatcherTimer _accountListScrollIdleTimer;
    private long _lastAccountListScrollTimestamp;
    private bool _isAccountListScrollActive;
    private bool _realizedAccountReportPending;
    private bool _usesNativeAccountList;

    public MainView()
    {
        InitializeComponent();
        _accountListScrollIdleTimer = new DispatcherTimer
        {
            Interval = AccountListScrollStateCheckInterval
        };
        _accountListScrollIdleTimer.Tick += AccountListScrollBecameIdle;
        AddHandler(KeyDownEvent, MainViewKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(
            AttachAccountListScrollViewer,
            DispatcherPriority.Loaded);
        DetachedFromVisualTree += (_, _) => DetachAccountListScrollViewer();
    }

    public void UseNativeAccountList(Control nativeAccountList)
    {
        ArgumentNullException.ThrowIfNull(nativeAccountList);
        _usesNativeAccountList = true;
        DetachAccountListScrollViewer();
        AccountList.IsVisible = false;
        AccountList.ItemsSource = null;
        NativeAccountListPresenter.Content = nativeAccountList;
        NativeAccountListPresenter.IsHitTestVisible = true;
    }

    public void UseNativeAccountGroupStrip(Control nativeAccountGroupStrip)
    {
        ArgumentNullException.ThrowIfNull(nativeAccountGroupStrip);
        if (AccountGroupsStrip.Parent is Panel parent)
            parent.Children.Remove(AccountGroupsStrip);
        NativeAccountGroupsPresenter.Content = nativeAccountGroupStrip;
        NativeAccountGroupsPresenter.IsHitTestVisible = true;
    }

    public void UseNativeGroupAccountPicker(Control nativeGroupAccountPicker)
    {
        ArgumentNullException.ThrowIfNull(nativeGroupAccountPicker);
        GroupAccountPicker.IsVisible = false;
        GroupAccountPicker.ItemsSource = null;
        NativeGroupAccountPickerPresenter.Content = nativeGroupAccountPicker;
        NativeGroupAccountPickerPresenter.IsHitTestVisible = true;
    }

    private void RefocusAccountSearchAfterClear(object? sender, RoutedEventArgs e) =>
        PostInputFocus(AccountSearchBox);

    private void RefocusGroupAccountSearchAfterClear(object? sender, RoutedEventArgs e) =>
        PostInputFocus(GroupAccountSearchBox);

    private void RefocusAccountPeriodAfterClear(object? sender, RoutedEventArgs e)
    {
        if ((sender as Visual)?.GetVisualAncestors().OfType<NumericUpDown>().FirstOrDefault()
            is { } periodInput)
        {
            PostInputFocus(periodInput);
        }
    }

    private static void PostInputFocus(Control input)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                var textBox = input as TextBox
                    ?? input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                (textBox as InputElement ?? input).Focus();
            },
            DispatcherPriority.Input);
    }

    private async void MainViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MobileShellViewModel viewModel) return;

        if (await viewModel.TryHandleBackNavigationAsync())
        {
            e.Handled = true;
            if (viewModel.IsSettingsVisible) ScrollMainViewToTop();
        }
    }

    private void FaqSectionExpanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Expander expanded) return;

        foreach (var section in new[] { FaqIconPacksExpander, FaqImportFormatsExpander })
        {
            if (!ReferenceEquals(section, expanded)) section.IsExpanded = false;
        }
    }

    private void ScrollMainViewToTop(object? sender, RoutedEventArgs e) =>
        ScrollMainViewToTop();

    private void ScrollMainViewToTop()
    {
        MainScrollViewer.Offset = new Vector(MainScrollViewer.Offset.X, 0);
        UpdateSettingsBackToTopVisibility();
    }

    private void MainScrollViewerChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateSettingsBackToTopVisibility();

    private void UpdateSettingsBackToTopVisibility()
    {
        SettingsBackToTopButton.IsVisible = DataContext is MobileShellViewModel
        {
            IsSettingsCategoryDetailVisible: true
        }
            && MainScrollViewer.Extent.Height > MainScrollViewer.Viewport.Height + 1
            && MainScrollViewer.Offset.Y > 24;
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
            UnlockMethodPasswordBox.FocusInput();

            DispatcherTimer.RunOnce(
                () => UnlockMethodPasswordBox.Classes.Remove(UnlockMethodAttentionClass),
                TimeSpan.FromMilliseconds(1200));
        }, DispatcherPriority.Background);
    }

    private void CopyAccountCode(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control
        {
                DataContext: MobileAccountItem account
            } control
            || DataContext is not MobileShellViewModel viewModel)
        {
            return;
        }

        var swipeRow = control.RenderTransform is TranslateTransform
            ? control
            : control.GetVisualAncestors()
                .OfType<Control>()
                .FirstOrDefault(candidate => candidate.RenderTransform is TranslateTransform);
        if (swipeRow?.RenderTransform is TranslateTransform transform
            && (ReferenceEquals(_swipedRow, swipeRow) || Math.Abs(transform.X) > 0))
        {
            ResetSwipe(swipeRow);
            e.Handled = true;
            return;
        }

        _ = viewModel.CopyAccountCodeAsync(account);
        e.Handled = true;
    }

    private void ToggleAccountFavorite(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MobileAccountItem account }
            && DataContext is MobileShellViewModel viewModel)
        {
            _ = viewModel.ToggleAccountFavoriteAsync(account);
        }

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

    private void AttachAccountListScrollViewer()
    {
        if (_usesNativeAccountList) return;
        AccountList.ApplyTemplate();
        var scrollViewer = AccountList
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (ReferenceEquals(_accountListScrollViewer, scrollViewer)) return;

        DetachAccountListScrollViewer();
        _accountListScrollViewer = scrollViewer;
        if (_accountListScrollViewer is not null)
            _accountListScrollViewer.ScrollChanged += AccountListScrolled;
        AccountList.ContainerPrepared += AccountContainerPrepared;
        AccountList.ContainerClearing += AccountContainerClearing;
        ScheduleRealizedAccountReport();
    }

    private void DetachAccountListScrollViewer()
    {
        _accountListScrollIdleTimer.Stop();
        _isAccountListScrollActive = false;
        if (_accountListScrollViewer is not null)
            _accountListScrollViewer.ScrollChanged -= AccountListScrolled;
        AccountList.ContainerPrepared -= AccountContainerPrepared;
        AccountList.ContainerClearing -= AccountContainerClearing;
        _accountListScrollViewer = null;
        if (DataContext is MobileShellViewModel viewModel)
        {
            viewModel.SetAccountListScrolling(false);
            viewModel.SetRealizedAccounts([]);
        }
    }

    private void AccountListScrolled(object? sender, ScrollChangedEventArgs e)
    {
        _lastAccountListScrollTimestamp = Stopwatch.GetTimestamp();
        if (_isAccountListScrollActive) return;

        _isAccountListScrollActive = true;
        if (DataContext is MobileShellViewModel viewModel)
            viewModel.SetAccountListScrolling(true);
        _accountListScrollIdleTimer.Start();
    }

    private void AccountListScrollBecameIdle(object? sender, EventArgs e)
    {
        if (Stopwatch.GetElapsedTime(_lastAccountListScrollTimestamp) < AccountListScrollIdleDelay)
            return;

        _accountListScrollIdleTimer.Stop();
        _isAccountListScrollActive = false;
        if (DataContext is MobileShellViewModel viewModel)
            viewModel.SetAccountListScrolling(false);
        ScheduleRealizedAccountReport();
    }

    private void AccountContainerPrepared(object? sender, ContainerPreparedEventArgs e) =>
        ScheduleRealizedAccountReport();

    private void AccountContainerClearing(object? sender, ContainerClearingEventArgs e) =>
        ScheduleRealizedAccountReport();

    private void ScheduleRealizedAccountReport()
    {
        if (_realizedAccountReportPending) return;
        _realizedAccountReportPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _realizedAccountReportPending = false;
            if (!_isAccountListScrollActive) ReportRealizedAccounts();
        }, DispatcherPriority.Background);
    }

    private void ReportRealizedAccounts()
    {
        if (DataContext is not MobileShellViewModel viewModel) return;
        var accounts = AccountList.GetRealizedContainers()
            .Select(container => container.DataContext)
            .OfType<MobileAccountItem>()
            .DistinctBy(account => account.Id)
            .ToArray();
        viewModel.SetRealizedAccounts(accounts);
    }

    private void ResetSwipe(Control? control)
    {
        if (control?.RenderTransform is TranslateTransform transform) transform.X = 0;
        if (ReferenceEquals(_openSwipeRow, control)) _openSwipeRow = null;
    }
}
