using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace TOTP.Avalonia.Shared.Controls;

/// <summary>Creates and retains a view on first use or during idle preloading.</summary>
public sealed class DeferredContent : Decorator
{
    public static readonly StyledProperty<bool> IsRequestedProperty =
        AvaloniaProperty.Register<DeferredContent, bool>(nameof(IsRequested));

    [Content]
    public IDataTemplate? Template { get; set; }

    public bool IsRequested
    {
        get => GetValue(IsRequestedProperty);
        set => SetValue(IsRequestedProperty, value);
    }

    public bool IsContentCreated => Child is not null;
    public event EventHandler? ContentCreated;

    public Control EnsureContentCreated()
    {
        VerifyAccess();
        if (Child is not null) return Child;
        Child = Template?.Build(DataContext)
            ?? throw new InvalidOperationException("The deferred view template is unavailable.");
        ContentCreated?.Invoke(this, EventArgs.Empty);
        return Child;
    }

    public void Preload(double availableWidth)
    {
        var content = EnsureContentCreated();
        if (availableWidth <= 0 || IsVisible) return;
        var wasVisible = content.IsVisible;
        try
        {
            content.SetCurrentValue(IsVisibleProperty, true);
            content.Measure(new Size(availableWidth, double.PositiveInfinity));
        }
        finally
        {
            content.SetCurrentValue(IsVisibleProperty, wasVisible);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRequestedProperty && change.GetNewValue<bool>())
            EnsureContentCreated();
    }
}
