using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TOTP.Avalonia.Desktop.Controls;

public partial class ProductTitleBar : UserControl
{
    private Size? _normalWindowSize;

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ProductTitleBar, string?>(nameof(Title));

    public static readonly StyledProperty<bool> ShowIconProperty =
        AvaloniaProperty.Register<ProductTitleBar, bool>(nameof(ShowIcon), true);

    public static readonly StyledProperty<bool> ShowMinimizeButtonProperty =
        AvaloniaProperty.Register<ProductTitleBar, bool>(nameof(ShowMinimizeButton));

    public static readonly StyledProperty<Thickness> TitlePaddingProperty =
        AvaloniaProperty.Register<ProductTitleBar, Thickness>(nameof(TitlePadding));

    public ProductTitleBar()
    {
        InitializeComponent();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowIcon
    {
        get => GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public bool ShowMinimizeButton
    {
        get => GetValue(ShowMinimizeButtonProperty);
        set => SetValue(ShowMinimizeButtonProperty, value);
    }

    public Thickness TitlePadding
    {
        get => GetValue(TitlePaddingProperty);
        set => SetValue(TitlePaddingProperty, value);
    }

    private void MoveWindow(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        if (e.ClickCount == 2 && window.CanResize)
        {
            ToggleMaximizedState(window);
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            window.BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void ToggleMaximizedState(Window window)
    {
        if (window.WindowState != WindowState.Maximized)
        {
            var currentSize = window.Bounds.Size;
            if (currentSize.Width > 0
                && currentSize.Height > 0
                && double.IsFinite(currentSize.Width)
                && double.IsFinite(currentSize.Height))
            {
                _normalWindowSize = currentSize;
            }

            window.WindowState = WindowState.Maximized;
            return;
        }

        window.WindowState = WindowState.Normal;
        if (_normalWindowSize is not { } normalSize) return;

        Dispatcher.UIThread.Post(
            () => RestoreNormalWindowSize(window, normalSize),
            DispatcherPriority.Loaded);
    }

    private static void RestoreNormalWindowSize(Window window, Size normalSize)
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return;

        window.Width = ClampRestoredLength(normalSize.Width, window.MinWidth, window.MaxWidth);
        window.Height = ClampRestoredLength(normalSize.Height, window.MinHeight, window.MaxHeight);
    }

    private static double ClampRestoredLength(double value, double minimum, double maximum)
    {
        var lowerBound = double.IsFinite(minimum) ? Math.Max(0, minimum) : 0;
        var upperBound = double.IsFinite(maximum)
            ? Math.Max(lowerBound, maximum)
            : double.MaxValue;
        return Math.Clamp(value, lowerBound, upperBound);
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Close();
    }

    private void MinimizeWindow(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.WindowState = WindowState.Minimized;
    }
}
