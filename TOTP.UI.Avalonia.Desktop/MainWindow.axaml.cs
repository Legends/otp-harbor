using Avalonia.Controls;
using Avalonia;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Platform;

namespace TOTP.Avalonia.Desktop;

public partial class MainWindow : Window
{
    private const double MaximumScreenWidthRatio = 0.92;
    private const double MaximumScreenHeightRatio = 0.60;
    private const double DefaultPageHeight = 540;
    private const double DefaultMinimumWidth = 360;
    private const double StandardMinimumHeight = 200;
    private static readonly TimeSpan HeightAnimationDuration = TimeSpan.FromMilliseconds(220);
    private bool _initialized;
    private bool _fitScheduled;
    private bool _editorFitScheduled;
    private MainWindowViewModel? _observedViewModel;
    private AccountListViewModel? _observedAccountList;
    private CancellationTokenSource? _heightAnimationLifetime;
    private SettingsWindow? _settingsWindow;
    private IDisposable? _settingsWindowRegistration;
    private IDisposable? _mainWindowActivityRegistration;
    private IDisposable? _settingsActivityRegistration;
    private bool _allowSettingsWindowClose;
    private Screen? _sizeLimitScreen;
    private PixelRect _sizeLimitWorkingArea;
    private double _sizeLimitScaling;

    public AvaloniaClipboardAccessor? ClipboardAccessor { get; init; }
    public AvaloniaWindowCoordinator? WindowCoordinator { get; init; }
    public AvaloniaActivityMonitor? ActivityMonitor { get; init; }

    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += MainWindowPositionChanged;
        ScalingChanged += MainWindowScalingChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowCoordinator?.RegisterMainWindow(this);
        _mainWindowActivityRegistration = ActivityMonitor?.Attach(this);
        if (Clipboard is not null)
            ClipboardAccessor?.Set(Clipboard);
        Screens.Changed += ScreensChanged;
        ApplyScreenSizeLimits(force: true);
        if (DataContext is MainWindowViewModel viewModel)
        {
            ObserveViewModel(viewModel);
            if (!_initialized)
            {
                _initialized = true;
                // Finish the native Show/activation sequence before opening OS prompts.
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible) viewModel.InitializeCommand.Execute(null);
                }, DispatcherPriority.Background);
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ObserveViewModel(null);
        _mainWindowActivityRegistration?.Dispose();
        _mainWindowActivityRegistration = null;
        _heightAnimationLifetime?.Cancel();
        Screens.Changed -= ScreensChanged;
        CloseSettingsWindow();
        WindowCoordinator?.UnregisterMainWindow(this);
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.PrepareForShutdown();
        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty
            && change.NewValue is WindowState.Minimized
            && DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.HandleWindowMinimizedAsync();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel settingsViewModel
            && settingsViewModel.IsSettingsVisible
            && e.Key == Key.Escape)
        {
            settingsViewModel.CloseSettingsCommand.Execute(null);
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        if (e.Key == Key.Escape
            && DataContext is MainWindowViewModel accountViewModel
            && accountViewModel.IsAccountListVisible
            && accountViewModel.AccountList.IsEditorVisible)
        {
            accountViewModel.AccountList.CancelEditCommand.Execute(null);
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        if (DataContext is MainWindowViewModel deleteViewModel
            && ShouldHandleAccountDeleteKey(
                e.Key,
                e.KeyModifiers,
                deleteViewModel.DeleteAccountCommand.CanExecute(null),
                IsTextEditingSource(e.Source)))
        {
            deleteViewModel.DeleteAccountCommand.Execute(null);
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        if (e.Key == Key.F
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && DataContext is MainWindowViewModel viewModel
            && viewModel.IsShellVisible)
        {
            viewModel.ToggleSearchCommand.Execute(null);
            FocusAccountSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape
            && DataContext is MainWindowViewModel searchViewModel
            && searchViewModel.IsSearchVisible)
        {
            searchViewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static bool ShouldHandleAccountDeleteKey(
        Key key,
        KeyModifiers modifiers,
        bool canDeleteSelectedAccount,
        bool isTextEditingSource) =>
        key == Key.Delete
        && modifiers == KeyModifiers.None
        && canDeleteSelectedAccount
        && !isTextEditingSource;

    private static bool IsTextEditingSource(object? source) =>
        source is TextBox
        || source is Visual visual
            && visual.GetVisualAncestors().OfType<TextBox>().Any();

    private void FocusAccountSearch(object? sender, RoutedEventArgs e) =>
        FocusAccountSearch();

    private void FocusAccountSearch()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !viewModel.IsShellVisible
            || viewModel.IsSettingsVisible)
            return;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!viewModel.IsSearchVisible) return;
                this.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(control => control.Name == "AccountSearchBox")
                    ?.Focus();
            },
            DispatcherPriority.Input);
    }

    private void ObserveViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_observedViewModel, viewModel)) return;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= MainViewModelPropertyChanged;
        if (_observedAccountList is not null)
            _observedAccountList.PropertyChanged -= AccountListPropertyChanged;

        _observedViewModel = viewModel;
        _observedAccountList = viewModel?.AccountList;

        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += MainViewModelPropertyChanged;
        if (_observedAccountList is not null)
            _observedAccountList.PropertyChanged += AccountListPropertyChanged;
    }

    private void MainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSettingsVisible))
        {
            if (_observedViewModel?.IsSettingsVisible == true)
                _heightAnimationLifetime?.Cancel();
            SyncSettingsWindow();
            if (_observedViewModel is { IsSettingsVisible: false, IsAccountListVisible: true })
            {
                Dispatcher.UIThread.Post(
                    ScheduleAccountPageFit,
                    DispatcherPriority.Background);
            }
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsPasswordUnlockVisible)
            && _observedViewModel?.IsPasswordUnlockVisible == true)
        {
            ScheduleSecretInputFocus("PasswordUnlockInput");
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsPasswordSetupVisible)
            && _observedViewModel?.IsPasswordSetupVisible == true)
        {
            ScheduleSecretInputFocus("PasswordSetupInput");
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.IsAccountListVisible)) return;
        if (_observedViewModel is { IsAccountListVisible: true })
            ScheduleAccountPageFit();
        else
        {
            MinHeight = Math.Min(StandardMinimumHeight, MaxHeight);
            StartHeightAnimation(Math.Min(MaxHeight, DefaultPageHeight));
        }
    }

    private void ScheduleSecretInputFocus(string controlName)
    {
        Dispatcher.UIThread.Post(
            () => this.GetVisualDescendants()
                .OfType<TOTP.Avalonia.Shared.Controls.RevealableSecretInput>()
                .FirstOrDefault(control => control.Name == controlName)
                ?.FocusInput(),
            DispatcherPriority.Loaded);
    }

    private void SyncSettingsWindow()
    {
        if (_observedViewModel?.IsSettingsVisible == true)
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = CreateSettingsWindow(_observedViewModel);
            _settingsWindowRegistration = WindowCoordinator?.RegisterOwnedDialog(_settingsWindow);
            _settingsActivityRegistration = ActivityMonitor?.Attach(_settingsWindow);
            _ = _settingsWindow.ShowDialog(this);
            return;
        }

        CloseSettingsWindow();
        Activate();
    }

    private SettingsWindow CreateSettingsWindow(MainWindowViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };
        window.Closing += SettingsWindowClosing;
        return window;
    }

    private void SettingsWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowSettingsWindowClose) return;
        e.Cancel = true;
        Dispatcher.UIThread.Post(
            () => _observedViewModel?.CloseSettingsCommand.Execute(null),
            DispatcherPriority.Input);
    }

    private void CloseSettingsWindow()
    {
        _settingsWindowRegistration?.Dispose();
        _settingsWindowRegistration = null;
        _settingsActivityRegistration?.Dispose();
        _settingsActivityRegistration = null;
        if (_settingsWindow is null) return;

        _allowSettingsWindowClose = true;
        _settingsWindow.Closing -= SettingsWindowClosing;
        _settingsWindow.Close();
        _settingsWindow = null;
        _allowSettingsWindowClose = false;
    }

    private void AccountListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountListViewModel.IsEditorVisible))
        {
            if (_observedAccountList?.IsEditorVisible == true)
            {
                ScheduleAccountEditorOpen();
            }
            else
            {
                SetAccountEditorPresented(false);
                ScheduleAccountPageFit();
            }

            return;
        }

        if (e.PropertyName is nameof(AccountListViewModel.Accounts)
            or nameof(AccountListViewModel.SelectedAccount)
            or nameof(AccountListViewModel.HasSelectedAccount)
            or nameof(AccountListViewModel.HasNoAccounts)
            or nameof(AccountListViewModel.HasNoSearchResults)
            or nameof(AccountListViewModel.HasCodeMessage))
        {
            ScheduleAccountPageFit();
        }
    }

    private void ScheduleAccountEditorOpen()
    {
        if (_editorFitScheduled) return;
        _editorFitScheduled = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _editorFitScheduled = false;
                OpenAccountEditor();
            },
            DispatcherPriority.Loaded);
    }

    private void OpenAccountEditor()
    {
        if (_observedViewModel is not { IsAccountListVisible: true }
            || _observedAccountList?.IsEditorVisible != true)
        {
            return;
        }

        PrepareAccountEditorForLayout();
        FitAccountEditorHeight();
        SetAccountEditorPresented(true);
    }

    private void FitAccountEditorHeight()
    {
        var flyout = FindAccountEditorFlyout();
        var editorContent = this.GetVisualDescendants()
            .OfType<StackPanel>()
            .FirstOrDefault(control => control.Name == "AccountEditorContent");
        if (flyout is null || editorContent is null) return;

        ApplyScreenSizeLimits();
        MinHeight = Math.Min(StandardMinimumHeight, MaxHeight);
        var availableWidth = Math.Max(
            0,
            flyout.Bounds.Width - flyout.Padding.Left - flyout.Padding.Right);
        editorContent.Measure(new Size(availableWidth, double.PositiveInfinity));
        var windowChromeHeight = Math.Max(0, Bounds.Height - flyout.Bounds.Height);
        var targetHeight = CalculateAccountEditorWindowHeight(
            windowChromeHeight,
            editorContent.DesiredSize.Height,
            flyout.Padding.Top + flyout.Padding.Bottom,
            MinHeight,
            MaxHeight);
        StartHeightAnimation(targetHeight);
    }

    private static double CalculateAccountEditorWindowHeight(
        double windowChromeHeight,
        double editorContentHeight,
        double editorVerticalPadding,
        double minimumHeight,
        double maximumHeight) =>
        Math.Clamp(
            windowChromeHeight + editorContentHeight + editorVerticalPadding + 2,
            minimumHeight,
            maximumHeight);

    private void SetAccountEditorPresented(bool isPresented)
    {
        var flyout = FindAccountEditorFlyout();
        if (flyout is null) return;

        var transform = flyout.RenderTransform as TranslateTransform;
        if (!isPresented)
        {
            flyout.IsVisible = false;
            flyout.IsEnabled = false;
            flyout.IsHitTestVisible = false;
            flyout.Classes.Remove("open");
            if (transform is not null) transform.X = GetFlyoutWidth(flyout);
            return;
        }

        flyout.IsEnabled = true;
        flyout.IsHitTestVisible = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_observedAccountList?.IsEditorVisible != true
                    || !flyout.IsVisible)
                {
                    return;
                }

                flyout.Classes.Add("open");
                Dispatcher.UIThread.Post(
                    () => flyout.GetVisualDescendants()
                        .OfType<TextBox>()
                        .FirstOrDefault(control => control.Name == "AccountIssuerBox")
                        ?.Focus(),
                    DispatcherPriority.Input);
            },
            DispatcherPriority.Render);
    }

    private void PrepareAccountEditorForLayout()
    {
        var flyout = FindAccountEditorFlyout();
        if (flyout is null) return;

        flyout.Classes.Remove("open");
        if (flyout.RenderTransform is TranslateTransform transform)
            transform.X = GetFlyoutWidth(flyout);
        flyout.IsEnabled = false;
        flyout.IsHitTestVisible = false;
        flyout.IsVisible = true;
        UpdateLayout();
    }

    private Border? FindAccountEditorFlyout() =>
        this.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(control => control.Name == "AccountEditorFlyout");

    private double GetFlyoutWidth(Border flyout) =>
        flyout.Bounds.Width > 0 ? flyout.Bounds.Width : Math.Max(0, Bounds.Width);

    private void ScheduleAccountPageFit()
    {
        if (_fitScheduled || _observedViewModel?.IsSettingsVisible == true) return;
        _fitScheduled = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _fitScheduled = false;
                FitAccountPageHeight();
            },
            DispatcherPriority.Loaded);
    }

    private void FitAccountPageHeight()
    {
        if (_observedViewModel?.IsSettingsVisible == true
            || !ShouldFitAccountPage(
                _observedViewModel is { IsAccountListVisible: true },
                _observedAccountList?.IsEditorVisible == true))
        {
            return;
        }

        ApplyScreenSizeLimits();
        MinHeight = Math.Min(StandardMinimumHeight, MaxHeight);
        UpdateLayout();

        var descendants = this.GetVisualDescendants().ToArray();
        var accountScroller = descendants
            .OfType<ScrollViewer>()
            .FirstOrDefault(control => control.Name == "AccountPageScrollViewer");
        var accountList = descendants
            .OfType<ContextPreservingAccountListBox>()
            .FirstOrDefault(control => control.Name == "AccountsListBox");
        var accountListRegion = descendants
            .OfType<Grid>()
            .FirstOrDefault(control => control.Name == "AccountListRegion");
        var accountContent = descendants
            .OfType<StackPanel>()
            .FirstOrDefault(control => control.Name == "AccountPageContent");
        if (accountScroller is null
            || accountList is null
            || accountListRegion is null
            || accountContent is null)
        {
            return;
        }

        var accountCount = _observedAccountList?.Accounts.Count ?? 0;
        var realizedRowHeight = accountList.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Select(item => item.Bounds.Height)
            .FirstOrDefault(height => height > 0);
        var rowHeight = realizedRowHeight > 0 ? realizedRowHeight : 42;
        var naturalListHeight = CalculateAccountListHeight(
            accountCount,
            rowHeight,
            double.PositiveInfinity);
        accountList.MaxHeight = naturalListHeight;
        accountList.InvalidateMeasure();
        accountListRegion.InvalidateMeasure();
        accountContent.InvalidateMeasure();
        UpdateLayout();
        var availableWidth = Math.Max(0, accountScroller.Bounds.Width);
        accountContent.Measure(new Size(availableWidth, double.PositiveInfinity));

        var windowChromeHeight = Math.Max(0, Bounds.Height - accountScroller.Bounds.Height);
        var fixedPageHeight = CalculateFixedAccountPageHeight(
            accountContent.DesiredSize.Height,
            accountListRegion.DesiredSize.Height);
        var availableListHeight = Math.Max(
            0,
            MaxHeight - windowChromeHeight - fixedPageHeight - 2);
        accountList.MaxHeight = CalculateAccountListHeight(
            accountCount,
            rowHeight,
            availableListHeight);
        accountContent.Measure(new Size(availableWidth, double.PositiveInfinity));

        var targetHeight = Math.Clamp(
            windowChromeHeight + accountContent.DesiredSize.Height + 2,
            MinHeight,
            MaxHeight);
        SetHeightImmediately(targetHeight);
    }

    private static bool ShouldFitAccountPage(
        bool isAccountListVisible,
        bool isEditorVisible) =>
        isAccountListVisible && !isEditorVisible;

    private static double CalculateAccountListHeight(
        int accountCount,
        double rowHeight,
        double availableHeight)
    {
        if (accountCount <= 0) return 0;

        var naturalHeight = (accountCount * rowHeight) + 2;
        var minimumVisibleHeight = Math.Min(naturalHeight, rowHeight + 2);
        return Math.Min(naturalHeight, Math.Max(minimumVisibleHeight, availableHeight));
    }

    private static double CalculateFixedAccountPageHeight(
        double contentHeight,
        double listRegionHeight) =>
        Math.Max(0, contentHeight - listRegionHeight);

    private void MainWindowPositionChanged(object? sender, PixelPointEventArgs e) =>
        ApplyScreenSizeLimits();

    private void MainWindowScalingChanged(object? sender, EventArgs e) =>
        ApplyScreenSizeLimits(force: true);

    private void ScreensChanged(object? sender, EventArgs e) =>
        ApplyScreenSizeLimits(force: true);

    private void ApplyScreenSizeLimits(bool force = false)
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        if (!force
            && ReferenceEquals(screen, _sizeLimitScreen)
            && screen.WorkingArea == _sizeLimitWorkingArea
            && Math.Abs(scale - _sizeLimitScaling) < 0.0001)
        {
            return;
        }

        _sizeLimitScreen = screen;
        _sizeLimitWorkingArea = screen.WorkingArea;
        _sizeLimitScaling = scale;
        var screenWidthLimit = Math.Max(
            1,
            (screen.WorkingArea.Width / scale) * MaximumScreenWidthRatio);
        var screenHeightLimit = (screen.WorkingArea.Height / scale) * MaximumScreenHeightRatio;
        MaxWidth = screenWidthLimit;
        MaxHeight = screenHeightLimit;
        MinWidth = Math.Min(DefaultMinimumWidth, MaxWidth);
        MinHeight = Math.Min(GetDesiredMinimumHeight(), MaxHeight);
        Width = Math.Clamp(Width, MinWidth, MaxWidth);
        Height = Math.Clamp(Height, MinHeight, MaxHeight);
    }

    private static double GetDesiredMinimumHeight() => StandardMinimumHeight;

    private void StartHeightAnimation(double targetHeight)
    {
        targetHeight = Math.Clamp(targetHeight, MinHeight, MaxHeight);
        _heightAnimationLifetime?.Cancel();
        var lifetime = new CancellationTokenSource();
        _heightAnimationLifetime = lifetime;
        _ = AnimateHeightAsync(targetHeight, lifetime);
    }

    private void SetHeightImmediately(double targetHeight)
    {
        targetHeight = Math.Clamp(targetHeight, MinHeight, MaxHeight);
        _heightAnimationLifetime?.Cancel();
        Height = targetHeight;
    }

    private async Task AnimateHeightAsync(
        double targetHeight,
        CancellationTokenSource lifetime)
    {
        var startHeight = Math.Max(MinHeight, Bounds.Height > 0 ? Bounds.Height : Height);
        if (Math.Abs(startHeight - targetHeight) < 0.5)
        {
            Height = targetHeight;
            CompleteHeightAnimation(lifetime);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (stopwatch.Elapsed < HeightAnimationDuration)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                var progress = Math.Clamp(
                    stopwatch.Elapsed.TotalMilliseconds / HeightAnimationDuration.TotalMilliseconds,
                    0,
                    1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                Height = startHeight + ((targetHeight - startHeight) * eased);
                await Task.Delay(16, lifetime.Token);
            }

            Height = targetHeight;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            CompleteHeightAnimation(lifetime);
        }
    }

    private void CompleteHeightAnimation(CancellationTokenSource lifetime)
    {
        if (ReferenceEquals(_heightAnimationLifetime, lifetime))
            _heightAnimationLifetime = null;
        lifetime.Dispose();
    }

}
