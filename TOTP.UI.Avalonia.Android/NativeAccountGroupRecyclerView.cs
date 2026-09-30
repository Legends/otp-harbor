using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using AndroidX.Core.Graphics;
using AndroidX.RecyclerView.Widget;
using Avalonia.Styling;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Shared.Styles;

namespace TOTP.Avalonia.Android;

internal sealed class NativeAccountGroupRecyclerView : RecyclerView
{
    private readonly MobileShellViewModel _viewModel;
    private readonly NativeAccountGroupAdapter _adapter;
    private bool _submitPending;
    private bool _disposed;

    public NativeAccountGroupRecyclerView(Context context, MobileShellViewModel viewModel)
        : base(context)
    {
        _viewModel = viewModel;
        _adapter = new NativeAccountGroupAdapter(viewModel);
        SetLayoutManager(new LinearLayoutManager(context, Horizontal, false));
        SetAdapter(_adapter);
        SetItemAnimator(null);
        HasFixedSize = true;
        SetItemViewCacheSize(8);
        GetRecycledViewPool().SetMaxRecycledViews(0, 8);
        HorizontalScrollBarEnabled = false;
        VerticalScrollBarEnabled = false;
        OverScrollMode = OverScrollMode.IfContentScrolls;

        _viewModel.Groups.CollectionChanged += GroupsChanged;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _adapter.ReplaceAll(CaptureRows());
    }

    public void DisposeHost()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.Groups.CollectionChanged -= GroupsChanged;
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _adapter.ReplaceAll([]);
        SetAdapter(null);
        Dispose();
    }

    private void GroupsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        ScheduleSubmit();

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MobileShellViewModel.FavoriteCount)
            or nameof(MobileShellViewModel.HasFavoriteAccounts)
            or nameof(MobileShellViewModel.IsFavoritesFilterSelected)
            or nameof(MobileShellViewModel.FavoritesText)
            or nameof(MobileShellViewModel.EditGroupText))
        {
            ScheduleSubmit();
        }
    }

    private void ScheduleSubmit()
    {
        if (_disposed || _submitPending) return;
        _submitPending = true;
        Post(() =>
        {
            _submitPending = false;
            if (!_disposed) _adapter.Submit(CaptureRows());
        });
    }

    private NativeAccountGroupRow[] CaptureRows()
    {
        var count = _viewModel.Groups.Count + (_viewModel.HasFavoriteAccounts ? 1 : 0);
        var rows = new List<NativeAccountGroupRow>(count);
        if (_viewModel.HasFavoriteAccounts)
        {
            rows.Add(NativeAccountGroupRow.Favorites(
                _viewModel.FavoritesText,
                _viewModel.FavoriteCount,
                _viewModel.IsFavoritesFilterSelected,
                _viewModel.ToggleFavoritesFilterCommand));
        }

        rows.AddRange(_viewModel.Groups.Select(NativeAccountGroupRow.FromGroup));
        return rows.ToArray();
    }
}

internal sealed class NativeAccountGroupAdapter : RecyclerView.Adapter
{
    private readonly MobileShellViewModel _viewModel;
    private NativeAccountGroupRow[] _items = [];

    public NativeAccountGroupAdapter(MobileShellViewModel viewModel)
    {
        _viewModel = viewModel;
        HasStableIds = true;
    }

    public override int ItemCount => _items.Length;

    public override long GetItemId(int position) => _items[position].StableId;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType) =>
        new GroupViewHolder(new NativeAccountGroupView(parent.Context!, _viewModel));

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) =>
        ((GroupViewHolder)holder).Bind(_items[position]);

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is GroupViewHolder groupHolder) groupHolder.Row.Unbind();
        base.OnViewRecycled(holder);
    }

    internal void ReplaceAll(NativeAccountGroupRow[] items)
    {
        _items = items;
        NotifyDataSetChanged();
    }

    internal void Submit(NativeAccountGroupRow[] items)
    {
        var diff = DiffUtil.CalculateDiff(new GroupDiffCallback(_items, items), false);
        _items = items;
        diff.DispatchUpdatesTo(this);
    }

    private sealed class GroupViewHolder(NativeAccountGroupView row) : RecyclerView.ViewHolder(row)
    {
        public NativeAccountGroupView Row { get; } = row;
        public void Bind(NativeAccountGroupRow item) => Row.Bind(item);
    }

}

internal sealed class GroupDiffCallback(
    NativeAccountGroupRow[] oldItems,
    NativeAccountGroupRow[] newItems) : DiffUtil.Callback
{
    public override int OldListSize => oldItems.Length;
    public override int NewListSize => newItems.Length;
    public override bool AreItemsTheSame(int oldItemPosition, int newItemPosition) =>
        oldItems[oldItemPosition].Id == newItems[newItemPosition].Id;
    public override bool AreContentsTheSame(int oldItemPosition, int newItemPosition) =>
        oldItems[oldItemPosition].VisualIdentity == newItems[newItemPosition].VisualIdentity;
}

internal sealed record NativeAccountGroupRow(
    Guid Id,
    bool IsFavorites,
    string Name,
    int AccountCount,
    bool IsSelected,
    string Color,
    ICommand SelectCommand,
    ICommand? EditCommand,
    string VisualIdentity)
{
    private static readonly Guid FavoritesId = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    public long StableId
    {
        get
        {
            Span<byte> bytes = stackalloc byte[16];
            Id.TryWriteBytes(bytes);
            return BitConverter.ToInt64(bytes);
        }
    }

    public static NativeAccountGroupRow Favorites(
        string name,
        int accountCount,
        bool isSelected,
        ICommand selectCommand) => new(
            FavoritesId,
            true,
            name,
            accountCount,
            isSelected,
            string.Empty,
            selectCommand,
            null,
            $"{name}\u001f{accountCount}\u001f{isSelected}");

    public static NativeAccountGroupRow FromGroup(MobileAccountGroupItem item) => new(
        item.Id,
        false,
        item.Name,
        item.AccountCount,
        item.IsSelected,
        item.Group.Color,
        item.SelectCommand,
        item.EditCommand,
        $"{item.Name}\u001f{item.AccountCount}\u001f{item.IsSelected}\u001f{item.Group.Color}");
}

internal sealed class NativeAccountGroupView : View
{
    private const int EditAccessibilityAction = 0x01021001;
    private const string EditIconData =
        "M4,17 L4,20 L7,20 L18,9 L15,6 Z M16,5 L18,3 L21,6 L19,8 Z";
    private readonly MobileShellViewModel _viewModel;
    private readonly Paint _fill = new(PaintFlags.AntiAlias);
    private readonly Paint _stroke = new(PaintFlags.AntiAlias);
    private readonly Paint _text = new(PaintFlags.AntiAlias);
    private readonly RectF _cardBounds = new();
    private readonly global::Android.Graphics.Path _starPath = new();
    private readonly global::Android.Graphics.Path _folderPath = new();
    private readonly global::Android.Graphics.Path _editPath = new();
    private readonly float _density;
    private readonly float _scaledDensity;
    private readonly int _touchSlop;
    private NativeAccountGroupRow? _row;
    private float _downX;
    private float _downY;
    private string _displayName = string.Empty;
    private string _displayCount = string.Empty;
    private Color _groupColor;

    public NativeAccountGroupView(Context context, MobileShellViewModel viewModel) : base(context)
    {
        _viewModel = viewModel;
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _scaledDensity = _density * (Resources?.Configuration?.FontScale ?? 1f);
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? Dp(8);
        Clickable = true;
        Focusable = true;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        _stroke.SetStyle(Paint.Style.Stroke);
        _stroke.StrokeJoin = Paint.Join.Round;
        _stroke.StrokeCap = Paint.Cap.Round;
    }

    public void Bind(NativeAccountGroupRow row)
    {
        _row = row;
        var width = Dp(row.IsFavorites ? 164 : 184);
        var height = Dp(56);
        if (LayoutParameters is not RecyclerView.LayoutParams parameters
            || parameters.Width != width
            || parameters.Height != height)
        {
            LayoutParameters = new RecyclerView.LayoutParams(width, height);
        }
        ContentDescription = row.Name;
        _displayCount = row.AccountCount.ToString();
        _groupColor = row.IsFavorites
            ? NativeGroupPalette.Current.FavoriteIcon
            : TryColor(row.Color, NativeGroupPalette.Current.Accent);
        PreparePaths();
        UpdateTextLayout();
        Invalidate();
    }

    public void Unbind()
    {
        _row = null;
        _displayName = string.Empty;
        _displayCount = string.Empty;
        ContentDescription = string.Empty;
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        _cardBounds.Set(Dp(2), Dp(2), width - Dp(2), height - Dp(2));
        PreparePaths();
        UpdateTextLayout();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var row = _row;
        if (row is null) return;
        var palette = NativeGroupPalette.Current;

        _fill.Color = row.IsFavorites
            ? (row.IsSelected ? palette.FavoriteSelected : palette.FavoriteBackground)
            : WithAlpha(_groupColor, 52);
        canvas.DrawRoundRect(_cardBounds, Dp(7), Dp(7), _fill);
        if (row.IsSelected)
        {
            _stroke.Color = palette.Accent;
            _stroke.StrokeWidth = Dp(1.25f);
            canvas.DrawRoundRect(_cardBounds, Dp(7), Dp(7), _stroke);
        }

        var iconColor = row.IsFavorites
            ? palette.FavoriteIcon
            : _groupColor;
        if (row.IsFavorites)
        {
            _fill.Color = iconColor;
            canvas.DrawPath(_starPath, _fill);
        }
        else
        {
            _stroke.Color = iconColor;
            _stroke.StrokeWidth = Dp(2);
            canvas.DrawPath(_folderPath, _stroke);
            _fill.Color = palette.Primary;
            canvas.DrawPath(_editPath, _fill);
        }

        _text.SetTypeface(Typeface.DefaultBold);
        _text.TextSize = Sp(15);
        _text.Color = row.IsFavorites ? palette.FavoriteForeground : palette.Primary;
        canvas.DrawText(_displayName, Dp(43), Height / 2f + Dp(5), _text);

        _text.SetTypeface(Typeface.Default);
        _text.TextSize = Sp(12);
        var countRight = row.IsFavorites ? Width - Dp(12) : Width - Dp(48);
        _text.TextAlign = Paint.Align.Right;
        canvas.DrawText(_displayCount, countRight, Height / 2f + Dp(5), _text);
        _text.TextAlign = Paint.Align.Left;
    }

    public override bool OnTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null || _row is null) return false;
        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = motionEvent.GetX();
                _downY = motionEvent.GetY();
                Pressed = true;
                return true;
            case MotionEventActions.Up:
                Pressed = false;
                if (Math.Abs(motionEvent.GetX() - _downX) <= _touchSlop
                    && Math.Abs(motionEvent.GetY() - _downY) <= _touchSlop)
                {
                    Invoke(motionEvent.GetX() >= Width - Dp(44) && !_row.IsFavorites
                        ? _row.EditCommand
                        : _row.SelectCommand);
                }
                return true;
            case MotionEventActions.Cancel:
                Pressed = false;
                return true;
            default:
                return true;
        }
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null || _row is null) return;
        info.ClassName = "android.widget.Button";
        info.Clickable = true;
        info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
        if (!_row.IsFavorites)
        {
            info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(
                EditAccessibilityAction,
                _viewModel.EditGroupText));
        }
    }

    public override bool PerformAccessibilityAction(
        global::Android.Views.Accessibility.Action action,
        Bundle? arguments)
    {
        if (_row is null) return base.PerformAccessibilityAction(action, arguments);
        if (action == global::Android.Views.Accessibility.Action.Click)
        {
            Invoke(_row.SelectCommand);
            return true;
        }
        if (action == (global::Android.Views.Accessibility.Action)EditAccessibilityAction
            && !_row.IsFavorites)
        {
            Invoke(_row.EditCommand);
            return true;
        }
        return base.PerformAccessibilityAction(action, arguments);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _starPath.Dispose();
            _folderPath.Dispose();
            _editPath.Dispose();
            _cardBounds.Dispose();
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Invoke(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
            Dispatcher.UIThread.Post(() => command.Execute(null));
    }

    private void UpdateTextLayout()
    {
        var row = _row;
        if (row is null || Width <= 0) return;
        _text.SetTypeface(Typeface.DefaultBold);
        _text.TextSize = Sp(15);
        var right = row.IsFavorites ? Width - Dp(42) : Width - Dp(78);
        _displayName = Ellipsize(row.Name, Math.Max(0, right - Dp(43)));
    }

    private string Ellipsize(string value, float width)
    {
        if (_text.MeasureText(value) <= width) return value;
        const string suffix = "…";
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_text.MeasureText(value.AsSpan(0, middle).ToString() + suffix) <= width)
                low = middle;
            else
                high = middle - 1;
        }
        return low == 0 ? suffix : value[..low] + suffix;
    }

    private void PreparePaths()
    {
        _starPath.Reset();
        var centerX = Dp(23);
        var centerY = Height / 2f;
        var radius = Dp(10);
        for (var index = 0; index < 10; index++)
        {
            var angle = -MathF.PI / 2 + index * MathF.PI / 5;
            var pointRadius = index % 2 == 0 ? radius : radius * 0.45f;
            var x = centerX + MathF.Cos(angle) * pointRadius;
            var y = centerY + MathF.Sin(angle) * pointRadius;
            if (index == 0) _starPath.MoveTo(x, y); else _starPath.LineTo(x, y);
        }
        _starPath.Close();

        _folderPath.Reset();
        _folderPath.MoveTo(centerX - Dp(10), centerY - Dp(7));
        _folderPath.LineTo(centerX - Dp(2), centerY - Dp(7));
        _folderPath.LineTo(centerX + Dp(1), centerY - Dp(4));
        _folderPath.LineTo(centerX + Dp(10), centerY - Dp(4));
        _folderPath.LineTo(centerX + Dp(10), centerY + Dp(8));
        _folderPath.LineTo(centerX - Dp(10), centerY + Dp(8));
        _folderPath.Close();

        _editPath.Reset();
        if (Width <= 0 || Height <= 0) return;
        using var source = PathParser.CreatePathFromPathData(EditIconData);
        if (source is null) return;
        using var bounds = new RectF();
        source.ComputeBounds(bounds, true);
        using var target = new RectF(
            Width - Dp(30),
            centerY - Dp(8),
            Width - Dp(14),
            centerY + Dp(8));
        using var matrix = new Matrix();
        matrix.SetRectToRect(bounds, target, Matrix.ScaleToFit.Center);
        source.Transform(matrix, _editPath);
    }

    private int Dp(float value) => (int)MathF.Round(value * _density);
    private float Sp(float value) => value * _scaledDensity;

    private static Color TryColor(string value, Color fallback)
    {
        try { return Color.ParseColor(value); }
        catch (ArgumentException) { return fallback; }
    }

    private static Color WithAlpha(Color value, byte alpha) =>
        Color.Argb(alpha, value.R, value.G, value.B);
}

internal sealed record NativeGroupPalette(
    Color Primary,
    Color Accent,
    Color FavoriteBackground,
    Color FavoriteSelected,
    Color FavoriteForeground,
    Color FavoriteIcon)
{
    private static readonly NativeGroupPalette Light = new(
        Color.ParseColor("#172033"), Color.ParseColor("#168AE0"),
        Color.ParseColor("#FFF4C2"), Color.ParseColor("#FFE89A"),
        Color.ParseColor("#9CA3AF"), Color.ParseColor("#FABD62"));
    private static readonly NativeGroupPalette Dark = new(
        Color.White, Color.ParseColor("#7D7FF4"),
        Color.ParseColor("#40361F"), Color.ParseColor("#514526"),
        Color.ParseColor("#E5E7EB"), Color.ParseColor("#FFD58A"));
    private static readonly NativeGroupPalette HighContrast = new(
        Color.White, Color.Yellow, Color.Black, Color.Black,
        Color.White, Color.Yellow);

    public static NativeGroupPalette Current
    {
        get
        {
            var theme = global::Avalonia.Application.Current?.ActualThemeVariant;
            if (theme == AvaloniaThemeVariants.HighContrast) return HighContrast;
            return theme == ThemeVariant.Dark ? Dark : Light;
        }
    }
}
