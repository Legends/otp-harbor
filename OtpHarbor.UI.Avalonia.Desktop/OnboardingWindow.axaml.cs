using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TOTP.Avalonia.Desktop;

public partial class OnboardingWindow : Window
{
    private const double DesiredWidth = 920;
    private const double DesiredHeight = 660;
    private const double DesiredMinimumWidth = 720;
    private const double DesiredMinimumHeight = 500;
    private const double WorkingAreaMargin = 24;

    public OnboardingWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToWorkingArea();
    }

    private void FitToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var availableWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - WorkingAreaMargin);
        var availableHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - WorkingAreaMargin);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        MinWidth = Math.Min(DesiredMinimumWidth, availableWidth);
        MinHeight = Math.Min(DesiredMinimumHeight, availableHeight);
        Width = Math.Min(DesiredWidth, availableWidth);
        Height = Math.Min(DesiredHeight, availableHeight);
    }
}
