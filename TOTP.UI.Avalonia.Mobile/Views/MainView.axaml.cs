using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Avalonia.Mobile.Views;

public partial class MainView : UserControl
{
    private const string UnlockMethodAttentionClass = "unlock-method-attention";
    private static readonly TimeSpan AccountListScrollIdleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AccountListScrollStateCheckInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ConfirmationScrollDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan StartupFadeDuration = TimeSpan.FromMilliseconds(360);
    private Control? _openSwipeRow;
    private Control? _swipedRow;
    private ScrollViewer? _accountListScrollViewer;
    private readonly DispatcherTimer _accountListScrollIdleTimer;
    private readonly DispatcherTimer _confirmationScrollTimer;
    private readonly DispatcherTimer _startupFadeTimer;
    private double _confirmationScrollStartY;
    private double _confirmationScrollTargetY;
    private long _confirmationScrollStartedAt;
    private long _lastAccountListScrollTimestamp;
    private bool _isAccountListScrollActive;
    private bool _realizedAccountReportPending;
    private bool _usesNativeAccountList;
    private bool _usesNativeAccountGroupStrip;
    private bool _backNavigationInProgress;
    private TopLevel? _backNavigationTopLevel;
    private IInputPane? _inputPane;
    private InputPaneState _inputPaneState;
    private Rect _inputPaneOccludedRect;
    private Control? _activePasswordConfirmationPanel;
    private Thickness? _mobileMainContentBaseMargin;
    private MobileShellViewModel? _nativeAccountListViewModel;
    private MobileShellViewModel? _startupViewModel;
    private bool _nativeAccountListRendered;
    private bool _startupTransitionPending;
    private bool _startupTransitionStarted;
    private bool _startupTransitionCompleted;
    private int _settingsBlendRevision;

    public event EventHandler? StartupContentRevealRequested;
    public event EventHandler? StartupTransitionCompleted;

    public MainView()
    {
        InitializeComponent();
        _accountListScrollIdleTimer = new DispatcherTimer
        {
            Interval = AccountListScrollStateCheckInterval
        };
        _accountListScrollIdleTimer.Tick += AccountListScrollBecameIdle;
        _confirmationScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _confirmationScrollTimer.Tick += AnimateConfirmationScroll;
        _startupFadeTimer = new DispatcherTimer
        {
            Interval = StartupFadeDuration
        };
        _startupFadeTimer.Tick += CompleteStartupTransition;
        AddHandler(KeyDownEvent, MainViewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, DismissAccountSearchFocus, RoutingStrategies.Tunnel);
        AttachedToVisualTree += MainViewAttachedToVisualTree;
        DetachedFromVisualTree += MainViewDetachedFromVisualTree;
        DataContextChanged += MainViewDataContextChanged;
    }

    private void MainViewAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _backNavigationTopLevel = TopLevel.GetTopLevel(this);
        if (_backNavigationTopLevel is not null)
        {
            _backNavigationTopLevel.BackRequested += MainViewBackRequested;
            _inputPane = _backNavigationTopLevel.InputPane;
            if (_inputPane is { } inputPane)
            {
                inputPane.StateChanged += InputPaneStateChanged;
                UpdateAccountBrandIconPickerInputPane(
                    inputPane.State,
                    inputPane.OccludedRect);
                UpdatePasswordConfirmationInputPane(
                    inputPane.State,
                    inputPane.OccludedRect);
            }
        }

        Dispatcher.UIThread.Post(
            AttachAccountListScrollViewer,
            DispatcherPriority.Loaded);
        EvaluateStartupTransition();
    }

    private void MainViewDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_inputPane is not null)
            _inputPane.StateChanged -= InputPaneStateChanged;
        _inputPane = null;
        _activePasswordConfirmationPanel = null;
        RestoreMobileMainContentMargin();
        if (_backNavigationTopLevel is not null)
            _backNavigationTopLevel.BackRequested -= MainViewBackRequested;
        _backNavigationTopLevel = null;
        _confirmationScrollTimer.Stop();
        DetachAccountListScrollViewer();
    }

    private void MainViewDataContextChanged(object? sender, EventArgs e)
    {
        if (_startupViewModel is not null)
            _startupViewModel.PropertyChanged -= StartupViewModelPropertyChanged;

        _startupViewModel = DataContext as MobileShellViewModel;
        if (_startupViewModel is not null)
            _startupViewModel.PropertyChanged += StartupViewModelPropertyChanged;
        EvaluateStartupTransition();
    }

    private void StartupViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MobileShellViewModel.IsSettingsVisible)
            or nameof(MobileShellViewModel.IsSettingsCategoryListVisible)
            or nameof(MobileShellViewModel.IsSettingsCategoryDetailVisible))
        {
            BlendInSettingsSurface();
        }

        if (e.PropertyName is nameof(MobileShellViewModel.IsBusy)
            or nameof(MobileShellViewModel.IsStartingVisible)
            or nameof(MobileShellViewModel.IsAccountListVisible)
            or nameof(MobileShellViewModel.IsInitialAccountLoadComplete)
            or nameof(MobileShellViewModel.HasAccounts)
            or nameof(MobileShellViewModel.HasNoAccounts))
        {
            BeginAccountReloadTransitionIfNeeded();
            EvaluateStartupTransition();
        }
    }

    private void BlendInSettingsSurface()
    {
        if (_startupViewModel?.IsSettingsVisible != true) return;

        var revision = ++_settingsBlendRevision;
        SettingsRoot.Opacity = 0;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (revision != _settingsBlendRevision
                    || _startupViewModel?.IsSettingsVisible != true)
                {
                    return;
                }

                SettingsRoot.Opacity = 1;
            },
            DispatcherPriority.Render);
    }

    private void BeginAccountReloadTransitionIfNeeded()
    {
        if (!_startupTransitionCompleted
            || _startupViewModel is not
            {
                IsBusy: true,
                IsAccountListVisible: true,
                IsInitialAccountLoadComplete: false
            })
        {
            return;
        }

        _startupFadeTimer.Stop();
        _startupTransitionCompleted = false;
        _startupTransitionStarted = false;
        _startupTransitionPending = false;
        _nativeAccountListRendered = false;
        StartupLoadingOverlay.Opacity = 1;
        StartupLoadingOverlay.IsVisible = true;
    }

    public void NotifyNativeAccountListRendered()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                _nativeAccountListRendered = true;
                EvaluateStartupTransition();
            },
            DispatcherPriority.Render);
    }

    private void EvaluateStartupTransition()
    {
        if (_startupTransitionCompleted || _startupTransitionStarted || _startupTransitionPending)
        {
            return;
        }

        if (!CanCompleteStartupTransition()) return;

        _startupTransitionPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _startupTransitionPending = false;
                if (_startupTransitionCompleted
                    || _startupTransitionStarted
                    || !CanCompleteStartupTransition())
                {
                    return;
                }
                _startupTransitionStarted = true;
                StartupContentRevealRequested?.Invoke(this, EventArgs.Empty);
                StartupLoadingOverlay.Opacity = 0;
                _startupFadeTimer.Start();
            },
            DispatcherPriority.Render);
    }

    private bool CanCompleteStartupTransition()
    {
        if (_startupViewModel is not { } viewModel
            || viewModel.IsBusy
            || viewModel.IsStartingVisible)
        {
            return false;
        }

        if (!viewModel.IsAccountListVisible) return true;
        if (!viewModel.IsInitialAccountLoadComplete) return false;
        if (!viewModel.HasAccounts && !viewModel.HasNoAccounts) return false;
        return !_usesNativeAccountList || !viewModel.HasAccounts || _nativeAccountListRendered;
    }

    private void CompleteStartupTransition(object? sender, EventArgs e)
    {
        _startupFadeTimer.Stop();
        if (_startupTransitionCompleted) return;

        _startupTransitionCompleted = true;
        StartupLoadingOverlay.IsVisible = false;
        StartupTransitionCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void InputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        UpdateAccountBrandIconPickerInputPane(e.NewState, e.EndRect);
        UpdatePasswordConfirmationInputPane(e.NewState, e.EndRect);
    }

    private void UpdateAccountBrandIconPickerInputPane(InputPaneState state, Rect occludedRect)
    {
        var bottomInset = state == InputPaneState.Open
            ? Math.Max(0, occludedRect.Height)
            : 0;
        AccountBrandIconPickerModalOverlay.Margin = new Thickness(0, 0, 0, bottomInset);
    }

    private void UpdatePasswordConfirmationInputPane(
        InputPaneState state,
        Rect occludedRect)
    {
        _inputPaneState = state;
        _inputPaneOccludedRect = occludedRect;
        if (state != InputPaneState.Open)
        {
            RestoreMobileMainContentMargin();
            return;
        }

        RepositionActivePasswordConfirmation();
    }

    private void RepositionActivePasswordConfirmation()
    {
        if (_inputPaneState != InputPaneState.Open
            || _activePasswordConfirmationPanel is not { IsVisible: true } confirmationPanel)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_inputPaneState != InputPaneState.Open
                    || !ReferenceEquals(_activePasswordConfirmationPanel, confirmationPanel)
                    || !confirmationPanel.IsVisible)
                {
                    return;
                }

                _mobileMainContentBaseMargin ??= MobileMainContent.Margin;
                var baseMargin = _mobileMainContentBaseMargin.Value;
                MobileMainContent.Margin = new Thickness(
                    baseMargin.Left,
                    baseMargin.Top,
                    baseMargin.Right,
                    baseMargin.Bottom + CalculateInputPaneOverlap());
                Dispatcher.UIThread.Post(
                    () => ScrollConfirmationIntoView(confirmationPanel),
                    DispatcherPriority.Render);
            },
            DispatcherPriority.Loaded);
    }

    private void RestoreMobileMainContentMargin()
    {
        MobileMainContent.ClearValue(Layoutable.MarginProperty);
        _mobileMainContentBaseMargin = null;
    }

    private double CalculateInputPaneOverlap()
    {
        if (_inputPaneState != InputPaneState.Open
            || _inputPaneOccludedRect.Height <= 0
            || MainScrollViewer.Viewport.Height <= 0)
        {
            return 0;
        }

        if (_backNavigationTopLevel is null
            || MainScrollViewer.TranslatePoint(default, _backNavigationTopLevel) is not { } origin)
        {
            return Math.Min(
                _inputPaneOccludedRect.Height,
                MainScrollViewer.Viewport.Height);
        }

        var viewportBottom = origin.Y + MainScrollViewer.Viewport.Height;
        return Math.Clamp(
            viewportBottom - _inputPaneOccludedRect.Top,
            0,
            Math.Min(_inputPaneOccludedRect.Height, MainScrollViewer.Viewport.Height));
    }

    public void UseNativeAccountList(Control nativeAccountList)
    {
        ArgumentNullException.ThrowIfNull(nativeAccountList);
        _usesNativeAccountList = true;
        DetachAccountListScrollViewer();
        AccountList.IsVisible = false;
        AccountList.IsHitTestVisible = false;
        NativeAccountListPresenter.Content = nativeAccountList;
        NativeAccountListPresenter.IsHitTestVisible = true;

        if (_nativeAccountListViewModel is not null)
            _nativeAccountListViewModel.PropertyChanged -= NativeAccountListStateChanged;
        _nativeAccountListViewModel = DataContext as MobileShellViewModel;
        if (_nativeAccountListViewModel is not null)
            _nativeAccountListViewModel.PropertyChanged += NativeAccountListStateChanged;
        UpdateAccountListConfirmationBackdrop();
    }

    private void NativeAccountListStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MobileShellViewModel.IsDeleteConfirmationVisible)
            or nameof(MobileShellViewModel.HasAccounts))
        {
            UpdateAccountListConfirmationBackdrop();
        }
    }

    private void UpdateAccountListConfirmationBackdrop()
    {
        if (!_usesNativeAccountList || _nativeAccountListViewModel is not { } viewModel) return;

        var showFallback = viewModel.IsSharedDeleteConfirmationVisible && viewModel.HasAccounts;
        AccountList.IsVisible = showFallback;
        if (_usesNativeAccountGroupStrip)
        {
            AccountGroupsFallbackPresenter.IsVisible = showFallback
                && viewModel.HasAccountNavigationCards;
        }

        if (showFallback && viewModel.SelectedAccount is { } selectedAccount)
            Dispatcher.UIThread.Post(() => AccountList.ScrollIntoView(selectedAccount));
    }

    public void UseNativeAccountGroupStrip(Control nativeAccountGroupStrip)
    {
        ArgumentNullException.ThrowIfNull(nativeAccountGroupStrip);
        _usesNativeAccountGroupStrip = true;
        AccountGroupsFallbackPresenter.IsVisible = false;
        NativeAccountGroupsPresenter.Content = nativeAccountGroupStrip;
        NativeAccountGroupsPresenter.IsHitTestVisible = true;
        UpdateAccountListConfirmationBackdrop();
    }

    public void UseNativeGroupAccountPicker(Control nativeGroupAccountPicker)
    {
        ArgumentNullException.ThrowIfNull(nativeGroupAccountPicker);
        GroupAccountPicker.IsVisible = false;
        GroupAccountPicker.ItemsSource = null;
        NativeGroupAccountPickerPresenter.Content = nativeGroupAccountPicker;
        NativeGroupAccountPickerPresenter.IsHitTestVisible = true;
    }

    private void KeepAccountSearchFocusedAfterClear(object? sender, RoutedEventArgs e) =>
        AccountSearchBox.Focus(NavigationMethod.Pointer);

    private void KeepGroupAccountSearchFocusedAfterClear(object? sender, RoutedEventArgs e) =>
        GroupAccountSearchBox.Focus(NavigationMethod.Pointer);

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

    private void DismissAccountSearchFocus(object? sender, PointerPressedEventArgs e)
    {
        if (!AccountSearchBox.IsKeyboardFocusWithin) return;

        var pointerPosition = e.GetPosition(AccountSearchBox);
        if (AccountSearchBox.Bounds.Contains(pointerPosition)) return;

        DismissAccountSearchFocus();
    }

    public void DismissAccountSearchFocus()
    {
        if (AccountSearchBox.IsKeyboardFocusWithin)
            Focus(NavigationMethod.Pointer);
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

    private async void MainViewBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MobileShellViewModel viewModel
            || !viewModel.CanHandleSystemBack)
        {
            return;
        }

        // Android must receive the handled result before awaiting navigation or it
        // may finish the activity while the view model is changing screens.
        e.Handled = true;
        if (_backNavigationInProgress) return;

        _backNavigationInProgress = true;
        try
        {
            if (await viewModel.TryHandleBackNavigationAsync()
                && viewModel.IsSettingsVisible)
            {
                ScrollMainViewToTop();
            }
        }
        finally
        {
            _backNavigationInProgress = false;
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

    private void FaqImportFormatSectionExpanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Expander expanded) return;

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

            HighlightPasswordInput(UnlockMethodPasswordBox);
        }, DispatcherPriority.Background);
    }

    private void RevealAppLockConfirmation(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not MobileShellViewModel viewModel) return;

            if (viewModel.IsDisableAppLockConfirmationVisible)
            {
                RevealPasswordConfirmation(
                    DisableAppLockConfirmationPanel,
                    DisableAppLockPasswordBox);
            }
            else if (viewModel is
                     {
                         IsAppLockReactivationPending: true,
                         IsBiometricEnrollmentVisible: true
                     })
            {
                RevealPasswordConfirmation(
                    UnlockMethodConfirmationPanel,
                    UnlockMethodPasswordBox);
            }
        }, DispatcherPriority.Background);
    }

    private void RevealPasswordConfirmation(
        Control confirmationPanel,
        RevealableSecretInput passwordInput)
    {
        _activePasswordConfirmationPanel = confirmationPanel;
        ScrollConfirmationIntoView(confirmationPanel);
        HighlightPasswordInput(passwordInput);
    }

    private void ScrollConfirmationIntoView(Control confirmationPanel)
    {
        var position = confirmationPanel.TranslatePoint(default, MainScrollViewer);
        if (position is null) return;

        const double topMargin = 12;
        const double bottomMargin = 12;
        var viewportHeight = Math.Max(
            0,
            MainScrollViewer.Viewport.Height - CalculateInputPaneOverlap());
        if (viewportHeight <= 0) return;

        var panelTop = position.Value.Y;
        var panelHeight = confirmationPanel.Bounds.Height;
        var panelBottom = panelTop + panelHeight;
        if (panelTop >= topMargin
            && panelBottom <= viewportHeight - bottomMargin)
        {
            return;
        }

        var maximumOffset = Math.Max(
            0,
            MainScrollViewer.Extent.Height - MainScrollViewer.Viewport.Height);
        var availableHeight = Math.Max(0, viewportHeight - topMargin - bottomMargin);
        var offsetDelta = panelHeight > availableHeight || panelTop < topMargin
            ? panelTop - topMargin
            : panelBottom - (viewportHeight - bottomMargin);
        var targetOffset = Math.Clamp(
            MainScrollViewer.Offset.Y + offsetDelta,
            0,
            maximumOffset);
        BeginConfirmationScrollAnimation(targetOffset);
    }

    private void BeginConfirmationScrollAnimation(double targetOffset)
    {
        _confirmationScrollTimer.Stop();
        _confirmationScrollStartY = MainScrollViewer.Offset.Y;
        _confirmationScrollTargetY = targetOffset;
        if (Math.Abs(_confirmationScrollTargetY - _confirmationScrollStartY) < 0.5)
        {
            return;
        }

        _confirmationScrollStartedAt = Stopwatch.GetTimestamp();
        _confirmationScrollTimer.Start();
    }

    private void AnimateConfirmationScroll(object? sender, EventArgs e)
    {
        var progress = Math.Clamp(
            Stopwatch.GetElapsedTime(_confirmationScrollStartedAt).TotalMilliseconds
                / ConfirmationScrollDuration.TotalMilliseconds,
            0,
            1);
        var easedProgress = 1 - Math.Pow(1 - progress, 3);
        var maximumOffset = Math.Max(
            0,
            MainScrollViewer.Extent.Height - MainScrollViewer.Viewport.Height);
        var currentOffset = Math.Clamp(
            _confirmationScrollStartY
                + ((_confirmationScrollTargetY - _confirmationScrollStartY) * easedProgress),
            0,
            maximumOffset);
        MainScrollViewer.Offset = new Vector(MainScrollViewer.Offset.X, currentOffset);
        UpdateSettingsBackToTopVisibility();

        if (progress >= 1)
        {
            _confirmationScrollTimer.Stop();
        }
    }

    private static void HighlightPasswordInput(RevealableSecretInput passwordInput)
    {
        passwordInput.Classes.Remove(UnlockMethodAttentionClass);
        passwordInput.Classes.Add(UnlockMethodAttentionClass);
        passwordInput.FocusInput();

        DispatcherTimer.RunOnce(
            () => passwordInput.Classes.Remove(UnlockMethodAttentionClass),
            TimeSpan.FromMilliseconds(1200));
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
