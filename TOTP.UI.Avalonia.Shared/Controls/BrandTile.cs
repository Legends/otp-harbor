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
    public BrandTile()
    {
        ActualThemeVariantChanged += (_, _) => RefreshRuntimeAppearance();
    }

    static BrandTile()
    {
        IconDataProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.UpdateIconPseudoClass());
        IconImageProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.UpdateIconPseudoClass());
        RuntimeIconProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.RefreshRuntimeAppearance());
        RuntimeAppearancesProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.RefreshRuntimeAppearance());
        TileBackgroundProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.RefreshRuntimeBackground());
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

    public static readonly StyledProperty<BrandRuntimeAppearances?> RuntimeAppearancesProperty =
        AvaloniaProperty.Register<BrandTile, BrandRuntimeAppearances?>(nameof(RuntimeAppearances));

    public static readonly DirectProperty<BrandTile, IImage?> RuntimeImageProperty =
        AvaloniaProperty.RegisterDirect<BrandTile, IImage?>(
            nameof(RuntimeImage),
            control => control.RuntimeImage);

    public static readonly DirectProperty<BrandTile, IBrush?> EffectiveTileBackgroundProperty =
        AvaloniaProperty.RegisterDirect<BrandTile, IBrush?>(
            nameof(EffectiveTileBackground),
            control => control.EffectiveTileBackground);

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

    public BrandRuntimeAppearances? RuntimeAppearances
    {
        get => GetValue(RuntimeAppearancesProperty);
        set => SetValue(RuntimeAppearancesProperty, value);
    }

    public IImage? RuntimeImage
    {
        get => _runtimeImage;
        private set => SetAndRaise(RuntimeImageProperty, ref _runtimeImage, value);
    }

    public IBrush? EffectiveTileBackground
    {
        get => _effectiveTileBackground;
        private set => SetAndRaise(
            EffectiveTileBackgroundProperty,
            ref _effectiveTileBackground,
            value);
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
    private IBrush? _effectiveTileBackground;
    private BrandRuntimeIcon? _effectiveRuntimeIcon;
    private RuntimeBrandIconImageCache.Lease? _runtimeLease;
    private CancellationTokenSource? _runtimeLoad;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshRuntimeAppearance();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRuntimeIconLoad();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshRuntimeAppearance()
    {
        var appearance = SelectRuntimeAppearance();
        var runtimeIcon = appearance?.Asset ?? RuntimeIcon;
        RefreshRuntimeBackground(appearance);
        PseudoClasses.Set(":themed-runtime", appearance is not null);
        if (Equals(_effectiveRuntimeIcon, runtimeIcon)
            && (_runtimeLease is not null || _runtimeLoad is not null || runtimeIcon is null))
            return;
        _effectiveRuntimeIcon = runtimeIcon;
        StartRuntimeIconLoad();
    }

    private void RefreshRuntimeBackground() => RefreshRuntimeBackground(SelectRuntimeAppearance());

    private void RefreshRuntimeBackground(BrandRuntimeAppearance? appearance)
    {
        if (appearance is null)
        {
            EffectiveTileBackground = TileBackground;
            return;
        }
        EffectiveTileBackground = appearance.BackgroundMode == BrandIconBackgroundMode.Solid
            && appearance.BackgroundColor is { } color
            && Color.TryParse(color, out var parsed)
                ? new SolidColorBrush(parsed)
                : Brushes.Transparent;
    }

    private BrandRuntimeAppearance? SelectRuntimeAppearance()
    {
        if (RuntimeAppearances is null) return null;
        var theme = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark
                    || ActualThemeVariant == global::TOTP.Avalonia.Shared.Styles.AvaloniaThemeVariants.HighContrast
            ? BrandIconTheme.Dark
            : BrandIconTheme.Light;
        return RuntimeAppearances.ForTheme(theme);
    }

    private void StartRuntimeIconLoad()
    {
        CancelRuntimeIconLoad();
        var runtimeIcon = _effectiveRuntimeIcon;
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
                    || !Equals(_effectiveRuntimeIcon, runtimeIcon)
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
