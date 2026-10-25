using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class BrandTile : TemplatedControl
{
    static BrandTile()
    {
        IconDataProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.UpdateIconPseudoClass());
        IconImageProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.UpdateIconPseudoClass());
        RuntimeIconProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.StartRuntimeIconLoad());
        IsCustomIconProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.PseudoClasses.Set(":custom-icon", control.IsCustomIcon));
    }

    public static readonly StyledProperty<IBrush?> TileBackgroundProperty =
        AvaloniaProperty.Register<BrandTile, IBrush?>(nameof(TileBackground));

    public static readonly StyledProperty<string?> IconDataProperty =
        AvaloniaProperty.Register<BrandTile, string?>(nameof(IconData));

    public static readonly StyledProperty<Transform?> IconTransformProperty =
        AvaloniaProperty.Register<BrandTile, Transform?>(nameof(IconTransform));

    public static readonly StyledProperty<IImage?> IconImageProperty =
        AvaloniaProperty.Register<BrandTile, IImage?>(nameof(IconImage));

    public static readonly StyledProperty<BrandRuntimeIcon?> RuntimeIconProperty =
        AvaloniaProperty.Register<BrandTile, BrandRuntimeIcon?>(nameof(RuntimeIcon));

    public static readonly DirectProperty<BrandTile, IImage?> RuntimeImageProperty =
        AvaloniaProperty.RegisterDirect<BrandTile, IImage?>(
            nameof(RuntimeImage),
            control => control.RuntimeImage);

    public static readonly StyledProperty<bool> IsCustomIconProperty =
        AvaloniaProperty.Register<BrandTile, bool>(nameof(IsCustomIcon));

    public static readonly StyledProperty<string> InitialsProperty =
        AvaloniaProperty.Register<BrandTile, string>(nameof(Initials), "?");

    public IBrush? TileBackground
    {
        get => GetValue(TileBackgroundProperty);
        set => SetValue(TileBackgroundProperty, value);
    }

    public string? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public Transform? IconTransform
    {
        get => GetValue(IconTransformProperty);
        set => SetValue(IconTransformProperty, value);
    }

    public IImage? IconImage
    {
        get => GetValue(IconImageProperty);
        set => SetValue(IconImageProperty, value);
    }

    public BrandRuntimeIcon? RuntimeIcon
    {
        get => GetValue(RuntimeIconProperty);
        set => SetValue(RuntimeIconProperty, value);
    }

    public IImage? RuntimeImage
    {
        get => _runtimeImage;
        private set => SetAndRaise(RuntimeImageProperty, ref _runtimeImage, value);
    }

    public bool IsCustomIcon
    {
        get => GetValue(IsCustomIconProperty);
        set => SetValue(IsCustomIconProperty, value);
    }

    public string Initials
    {
        get => GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value ?? "?");
    }

    private IImage? _runtimeImage;
    private RuntimeBrandIconImageCache.Lease? _runtimeLease;
    private CancellationTokenSource? _runtimeLoad;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (RuntimeIcon is not null && _runtimeLease is null) StartRuntimeIconLoad();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRuntimeIconLoad();
        base.OnDetachedFromVisualTree(e);
    }

    private void StartRuntimeIconLoad()
    {
        CancelRuntimeIconLoad();
        var runtimeIcon = RuntimeIcon;
        if (runtimeIcon is null) return;

        var cancellation = new CancellationTokenSource();
        _runtimeLoad = cancellation;
        _ = LoadRuntimeIconAsync(runtimeIcon, cancellation);
    }

    private async Task LoadRuntimeIconAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationTokenSource cancellation)
    {
        RuntimeBrandIconImageCache.Lease? lease = null;
        try
        {
            lease = await RuntimeBrandIconImageCache.AcquireAsync(
                runtimeIcon,
                cancellation.Token).ConfigureAwait(false);
            if (lease is null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested
                    || !Equals(RuntimeIcon, runtimeIcon)
                    || !ReferenceEquals(_runtimeLoad, cancellation))
                    return;

                _runtimeLease = lease;
                RuntimeImage = lease.Image;
                lease = null;
                UpdateIconPseudoClass();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lease?.Dispose();
            if (ReferenceEquals(_runtimeLoad, cancellation))
            {
                _runtimeLoad = null;
                cancellation.Dispose();
            }
        }
    }

    private void CancelRuntimeIconLoad()
    {
        var cancellation = Interlocked.Exchange(ref _runtimeLoad, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        Interlocked.Exchange(ref _runtimeLease, null)?.Dispose();
        RuntimeImage = null;
        UpdateIconPseudoClass();
    }

    private void UpdateIconPseudoClass() =>
        PseudoClasses.Set(
            ":has-icon",
            !string.IsNullOrWhiteSpace(IconData) || IconImage is not null || RuntimeImage is not null);
}
