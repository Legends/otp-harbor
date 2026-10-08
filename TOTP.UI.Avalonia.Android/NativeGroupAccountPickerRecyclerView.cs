using System.Collections.Specialized;
using System.ComponentModel;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.Accessibility;
using AndroidX.RecyclerView.Widget;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class NativeGroupAccountPickerRecyclerView : RecyclerView
{
    private readonly MobileShellViewModel _viewModel;
    private readonly NativeGroupAccountPickerAdapter _adapter;
    private bool _disposed;

    public NativeGroupAccountPickerRecyclerView(Context context, MobileShellViewModel viewModel)
        : base(context)
    {
        _viewModel = viewModel;
        _adapter = new NativeGroupAccountPickerAdapter(viewModel);
        SetLayoutManager(new LinearLayoutManager(context, Vertical, false));
        SetAdapter(_adapter);
        SetItemAnimator(null);
        HasFixedSize = true;
        SetItemViewCacheSize(12);
        GetRecycledViewPool().SetMaxRecycledViews(
            NativeGroupAccountPickerAdapter.AccountSelectionViewType,
            12);
        OverScrollMode = OverScrollMode.IfContentScrolls;
        VerticalScrollBarEnabled = false;

        _viewModel.GroupEditorAccounts.CollectionChanged += AccountsChanged;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _adapter.ReplaceAll(CaptureRows());
        UpdateModalVisibility();
    }

    public void DisposeHost()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.GroupEditorAccounts.CollectionChanged -= AccountsChanged;
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _adapter.DisposeAdapter();
        SetAdapter(null);
        Dispose();
    }

    private void AccountsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed) return;
        var rows = CaptureRows();
        Post(() =>
        {
            if (!_disposed) _adapter.ReplaceAll(rows);
        });
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || args.PropertyName != nameof(MobileShellViewModel.IsDeleteGroupConfirmationVisible))
            return;

        Post(UpdateModalVisibility);
    }

    private void UpdateModalVisibility()
    {
        if (_disposed) return;

        var isBlocked = _viewModel.IsDeleteGroupConfirmationVisible;
        Enabled = !isBlocked;
        Visibility = isBlocked ? ViewStates.Invisible : ViewStates.Visible;
        if (isBlocked) StopScroll();
    }

    private MobileGroupAccountSelection[] CaptureRows() =>
        _viewModel.GroupEditorAccounts.ToArray();
}

internal sealed class NativeGroupAccountPickerAdapter : RecyclerView.Adapter
{
    internal const int AccountSelectionViewType = 2;
    private readonly MobileShellViewModel _viewModel;
    private MobileGroupAccountSelection[] _items = [];

    public NativeGroupAccountPickerAdapter(MobileShellViewModel viewModel)
    {
        _viewModel = viewModel;
        HasStableIds = true;
    }

    public override int ItemCount => _items.Length;

    public override long GetItemId(int position)
    {
        Span<byte> bytes = stackalloc byte[16];
        _items[position].AccountId.TryWriteBytes(bytes);
        return BitConverter.ToInt64(bytes);
    }

    public override int GetItemViewType(int position) => AccountSelectionViewType;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var row = new NativeGroupAccountSelectionRowView(parent.Context!, _viewModel)
        {
            LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                NativeGroupAccountSelectionRowView.RowHeight(parent.Context!))
        };
        return new AccountSelectionViewHolder(row);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) =>
        ((AccountSelectionViewHolder)holder).Bind(_items[position]);

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is AccountSelectionViewHolder selectionHolder) selectionHolder.Unbind();
        base.OnViewRecycled(holder);
    }

    internal void ReplaceAll(MobileGroupAccountSelection[] items)
    {
        _items = items;
        NotifyDataSetChanged();
    }

    internal void DisposeAdapter()
    {
        _items = [];
        NotifyDataSetChanged();
    }

    private sealed class AccountSelectionViewHolder(NativeGroupAccountSelectionRowView row)
        : RecyclerView.ViewHolder(row)
    {
        public void Bind(MobileGroupAccountSelection item) => row.Bind(item);
        public void Unbind() => row.Unbind();
    }
}

internal sealed class NativeGroupAccountSelectionRowView : View
{
    private readonly MobileShellViewModel _viewModel;
    private readonly Paint _fill = new(PaintFlags.AntiAlias);
    private readonly Paint _stroke = new(PaintFlags.AntiAlias);
    private readonly Paint _text = new(PaintFlags.AntiAlias);
    private readonly RectF _brandBounds = new();
    private readonly float _density;
    private readonly float _scaledDensity;
    private readonly int _touchSlop;
    private MobileGroupAccountSelection? _item;
    private NativeBrandIcon? _brandIcon;
    private Color _brandColor;
    private float _downX;
    private float _downY;
    private bool _moved;

    public NativeGroupAccountSelectionRowView(
        Context context,
        MobileShellViewModel viewModel)
        : base(context)
    {
        _viewModel = viewModel;
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _scaledDensity = _density * (Resources?.Configuration?.FontScale ?? 1f);
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? Dp(8);
        _stroke.SetStyle(Paint.Style.Stroke);
        _stroke.StrokeCap = Paint.Cap.Round;
        _stroke.StrokeJoin = Paint.Join.Round;
        Clickable = true;
        Focusable = true;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        SetWillNotDraw(false);
    }

    public static int RowHeight(Context context)
    {
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)MathF.Round(62f * density);
    }

    public void Bind(MobileGroupAccountSelection item)
    {
        if (ReferenceEquals(_item, item))
        {
            Invalidate();
            return;
        }

        Unbind();
        _item = item;
        _item.PropertyChanged += ItemPropertyChanged;
        _brandColor = TryColor(item.Brand.BackgroundColor, NativeAccountPalette.Current.GenericBrand);
        PrepareBrandIcon();
        ContentDescription = string.IsNullOrWhiteSpace(item.AccountName)
            ? item.Issuer
            : $"{item.Issuer}, {item.AccountName}";
        Invalidate();
    }

    public void Unbind()
    {
        if (_item is not null) _item.PropertyChanged -= ItemPropertyChanged;
        _item = null;
        _brandIcon?.Dispose();
        _brandIcon = null;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_item is null) return;

        var palette = NativeAccountPalette.Current;
        _fill.Color = palette.Card;
        canvas.DrawRect(0, 0, Width, Height, _fill);

        DrawCheckbox(canvas, palette);
        var textLeft = Dp(56);
        if (_item.ShowIssuerLogo)
        {
            DrawBrand(canvas);
            textLeft = Dp(108);
        }
        DrawText(canvas, textLeft, palette);

        _stroke.Color = palette.Border;
        _stroke.StrokeWidth = Math.Max(1, Dp(0.6f));
        canvas.DrawLine(Dp(8), Height - _stroke.StrokeWidth, Width - Dp(8), Height - _stroke.StrokeWidth, _stroke);
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        var size = Dp(40);
        var left = Dp(56);
        _brandBounds.Set(left, (height - size) / 2f, left + size, (height + size) / 2f);
        PrepareBrandIcon();
    }

    public override bool OnTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null || _item is null || !_viewModel.IsGroupEditorVisible)
            return false;

        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = motionEvent.GetX();
                _downY = motionEvent.GetY();
                _moved = false;
                Pressed = true;
                return true;
            case MotionEventActions.Move:
                if (Math.Abs(motionEvent.GetX() - _downX) > _touchSlop
                    || Math.Abs(motionEvent.GetY() - _downY) > _touchSlop)
                {
                    _moved = true;
                    Pressed = false;
                }
                return true;
            case MotionEventActions.Up:
                Pressed = false;
                if (!_moved) PerformClick();
                return true;
            case MotionEventActions.Cancel:
                Pressed = false;
                return true;
            default:
                return base.OnTouchEvent(motionEvent);
        }
    }

    public override bool PerformClick()
    {
        base.PerformClick();
        if (_item is null || _viewModel.IsDeleteGroupConfirmationVisible) return false;
        _item.IsSelected = !_item.IsSelected;
        return true;
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null || _item is null) return;
        info.ClassName = "android.widget.CheckBox";
        info.Checkable = true;
        if (OperatingSystem.IsAndroidVersionAtLeast(36))
        {
            info.CheckedState = _item.IsSelected
                ? CheckedState.True
                : CheckedState.False;
        }
        else
        {
            info.Checked = _item.IsSelected;
        }
        info.Clickable = true;
        info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _brandBounds.Dispose();
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawCheckbox(Canvas canvas, NativeAccountPalette palette)
    {
        var size = Dp(24);
        var left = Dp(14);
        var top = (Height - size) / 2f;
        using var bounds = new RectF(left, top, left + size, top + size);
        _stroke.StrokeWidth = Dp(2);
        _stroke.Color = _item!.IsSelected ? palette.Accent : palette.Secondary;

        if (_item.IsSelected)
        {
            _fill.Color = palette.Accent;
            canvas.DrawRoundRect(bounds, Dp(5), Dp(5), _fill);
            _stroke.Color = Color.White;
            _stroke.StrokeWidth = Dp(2.2f);
            canvas.DrawLine(left + Dp(5), top + Dp(12), left + Dp(10), top + Dp(17), _stroke);
            canvas.DrawLine(left + Dp(10), top + Dp(17), left + Dp(19), top + Dp(7), _stroke);
            return;
        }

        canvas.DrawRoundRect(bounds, Dp(5), Dp(5), _stroke);
    }

    private void DrawBrand(Canvas canvas)
    {
        _fill.Color = _brandColor;
        canvas.DrawRoundRect(_brandBounds, Dp(8), Dp(8), _fill);
        if (_brandIcon is not null)
        {
            _brandIcon.Draw(canvas, _fill, _stroke);
            return;
        }

        _text.Color = Color.White;
        _text.TextAlign = Paint.Align.Center;
        _text.TextSize = Sp(14);
        _text.SetTypeface(Typeface.DefaultBold);
        var baseline = _brandBounds.CenterY() - (_text.Ascent() + _text.Descent()) / 2;
        canvas.DrawText(_item!.Brand.Initials, _brandBounds.CenterX(), baseline, _text);
        _text.TextAlign = Paint.Align.Left;
    }

    private void DrawText(Canvas canvas, float left, NativeAccountPalette palette)
    {
        var available = Math.Max(0, Width - left - Dp(12));
        _text.TextAlign = Paint.Align.Left;
        _text.SetTypeface(Typeface.DefaultBold);
        _text.TextSize = Sp(15);
        _text.Color = palette.Primary;
        canvas.DrawText(Ellipsize(_item!.Issuer, available), left, Dp(25), _text);

        _text.SetTypeface(Typeface.Default);
        _text.TextSize = Sp(12);
        _text.Color = palette.Secondary;
        canvas.DrawText(Ellipsize(_item.AccountName, available), left, Dp(46), _text);
    }

    private string Ellipsize(string value, float availableWidth)
    {
        if (value.Length == 0 || availableWidth <= 0) return string.Empty;
        if (_text.MeasureText(value) <= availableWidth) return value;
        const string suffix = "…";
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_text.MeasureText(value.AsSpan(0, middle).ToString() + suffix) <= availableWidth)
                low = middle;
            else
                high = middle - 1;
        }
        return low == 0 ? suffix : value[..low] + suffix;
    }

    private void PrepareBrandIcon()
    {
        _brandIcon?.Dispose();
        _brandIcon = null;
        if (_item is not { ShowIssuerLogo: true }
            || Height <= 0)
        {
            return;
        }

        using var target = new RectF(
            _brandBounds.Left + Dp(8),
            _brandBounds.Top + Dp(8),
            _brandBounds.Right - Dp(8),
            _brandBounds.Bottom - Dp(8));
        _brandIcon = NativeBrandIcon.TryCreate(_item.Brand, target, Color.White);
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MobileGroupAccountSelection.IsSelected))
        {
            SendAccessibilityEvent(EventTypes.WindowContentChanged);
            PostInvalidate();
        }
    }

    private int Dp(float value) => (int)MathF.Round(value * _density);
    private float Sp(float value) => value * _scaledDensity;

    private static Color TryColor(string value, Color fallback)
    {
        try
        {
            return Color.ParseColor(value);
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }
}
