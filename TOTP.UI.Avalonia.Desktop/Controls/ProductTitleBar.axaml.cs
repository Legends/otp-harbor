using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TOTP.Avalonia.Desktop.Controls;

public partial class ProductTitleBar : UserControl
{
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
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            window.BeginMoveDrag(e);
            e.Handled = true;
        }
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
