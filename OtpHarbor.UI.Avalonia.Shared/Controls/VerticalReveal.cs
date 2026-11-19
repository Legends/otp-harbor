using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class VerticalReveal : Decorator
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<VerticalReveal, bool>(nameof(IsExpanded));
    public static readonly StyledProperty<double> RevealProgressProperty =
        AvaloniaProperty.Register<VerticalReveal, double>(nameof(RevealProgress));
    private double _contentHeight;

    static VerticalReveal() => AffectsMeasure<VerticalReveal>(RevealProgressProperty);

    public VerticalReveal()
    {
        ClipToBounds = true;
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RevealProgressProperty,
                Duration = TimeSpan.FromMilliseconds(220),
                Easing = new CubicEaseOut()
            }
        };
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public double RevealProgress => Math.Clamp(GetValue(RevealProgressProperty), 0, 1);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
        {
            UpdateChildVisibility();
            // Keep the target at local priority while a transition owns the animated value.
            SetValue(RevealProgressProperty, IsExpanded ? 1d : 0d);
        }
        if (change.Property == ChildProperty || change.Property == RevealProgressProperty)
            UpdateChildVisibility();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { IsVisible: true } child) return default;
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        _contentHeight = child.DesiredSize.Height;
        return new Size(child.DesiredSize.Width, _contentHeight * RevealProgress);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { IsVisible: true } child)
            child.Arrange(new Rect(new Size(finalSize.Width, Math.Max(_contentHeight, finalSize.Height))));
        return finalSize;
    }

    private void UpdateChildVisibility()
    {
        if (Child is { } child)
            child.SetCurrentValue(IsVisibleProperty, IsExpanded || RevealProgress > 0);
        SetCurrentValue(IsHitTestVisibleProperty, IsExpanded);
    }
}
