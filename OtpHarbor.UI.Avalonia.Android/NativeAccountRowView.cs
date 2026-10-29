using System.ComponentModel;
using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using AndroidX.Core.Graphics;
using Avalonia.Styling;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Avalonia.Shared.Styles;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Android;

internal sealed class NativeAccountRowView : View
{
    private const string EditIconData =
        "M4,17 L4,20 L7,20 L18,9 L15,6 Z M16,5 L18,3 L21,6 L19,8 Z";
    internal static readonly Java.Lang.Object LocalizationPayload =
        new Java.Lang.String("localization");

    private const int FavoriteAccessibilityAction = 0x01020001;
    private const int QrAccessibilityAction = 0x01020002;
    private const int EditAccessibilityAction = 0x01020003;
    private const int DeleteAccessibilityAction = 0x01020004;

    private readonly MobileShellViewModel _viewModel;
    private readonly NativeBrandIconCache _brandIcons;
    private readonly Action<NativeAccountRowView> _openRowChanged;
    private readonly Paint _fill = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    private readonly Paint _stroke = new(PaintFlags.AntiAlias) { StrokeWidth = 1 };
    private readonly Paint _text = new(PaintFlags.AntiAlias);
    private readonly Paint _icon = new(PaintFlags.AntiAlias);
    private readonly RectF _cardBounds = new();
    private readonly RectF _brandBounds = new();
    private readonly global::Android.Graphics.Path _cardClipPath = new();
    private readonly global::Android.Graphics.Path _starPath = new();
    private readonly global::Android.Graphics.Path _editPath = new();
    private readonly int _touchSlop;
    private readonly float _density;
    private readonly float _fontScale;
    private NativeAccountRow? _row;
    private ValueAnimator? _settleAnimator;
    private ValueAnimator? _highlightAnimator;
    private float _highlightStrength;
    private float _downX;
    private float _downY;
    private float _startOffset;
    private float _swipeOffset;
    private bool _horizontalGesture;
    private bool _animating;
    private bool _isListScrolling;
    private NativeBrandIcon? _brandIcon;
    private string _displayIssuer = string.Empty;
    private string _displaySecondary = string.Empty;
    private string _displayCode = string.Empty;
    private string _displayNextCode = string.Empty;
    private string _rawCode = string.Empty;
    private Color _brandColor;
    private bool _drawBrandBackground = true;
    private string? _preparedRuntimeCacheKey;
    private bool _isSelected;

    public NativeAccountRowView(
        Context context,
        MobileShellViewModel viewModel,
        NativeBrandIconCache brandIcons,
        Action<NativeAccountRowView> openRowChanged)
        : base(context)
    {
        _viewModel = viewModel;
        _brandIcons = brandIcons;
        _openRowChanged = openRowChanged;
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _fontScale = Resources?.Configuration?.FontScale ?? 1f;
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? Dp(8);
        Clickable = true;
        Focusable = true;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        _stroke.SetStyle(Paint.Style.Stroke);
        _icon.SetStyle(Paint.Style.Stroke);
        _icon.StrokeCap = Paint.Cap.Round;
        _icon.StrokeJoin = Paint.Join.Round;
    }

    public bool IsActionsOpen => _swipeOffset > 0;

    public static int RowHeight(Context context)
    {
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)MathF.Round(84f * density);
    }

    public static RectF CreateBrandIconTarget(Context context)
    {
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(float value) => (int)MathF.Round(value * density);
        var centerY = RowHeight(context) / 2f;
        var brandLeft = Dp(2) + Dp(10);
        var brandTop = centerY - Dp(44) / 2f;
        return new RectF(
            brandLeft + Dp(9),
            brandTop + Dp(9),
            brandLeft + Dp(44) - Dp(9),
            brandTop + Dp(44) - Dp(9));
    }

    public void Bind(NativeAccountRow row, bool isListScrolling)
    {
        if (ReferenceEquals(_row?.Account, row.Account))
        {
            _row = row;
            SetListScrolling(isListScrolling);
            UpdateAccessibilityDescription();
            Invalidate();
            return;
        }

        StopAnimating();
        Unsubscribe();
        _row = row;
        _isListScrolling = isListScrolling;
        _row.Account.PropertyChanged += AccountPropertyChanged;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _isSelected = _viewModel.SelectedAccount?.Id == row.Id;
        _swipeOffset = 0;
        _rawCode = row.Account.Code;
        _displayCode = row.Account.DisplayCode;
        _displayNextCode = row.Account.DisplayNextCode;
        _preparedRuntimeCacheKey = null;
        UpdateBrandAppearance(row.Account.Brand);
        PrepareBrandIcon();
        UpdateTextLayout();
        UpdateStarPath();
        PrepareEditPath();
        UpdateAccessibilityDescription();
        StartAnimating();
        Invalidate();
    }

    public void Unbind()
    {
        StopHighlight();
        StopAnimating();
        Unsubscribe();
        _row = null;
        _swipeOffset = 0;
        _brandIcon?.Dispose();
        _brandIcon = null;
        _preparedRuntimeCacheKey = null;
        _displayIssuer = string.Empty;
        _displaySecondary = string.Empty;
        _displayCode = string.Empty;
        _displayNextCode = string.Empty;
        _rawCode = string.Empty;
        _isSelected = false;
        _isListScrolling = false;
        _starPath.Reset();
        ContentDescription = string.Empty;
        _settleAnimator?.Cancel();
        _settleAnimator?.Dispose();
        _settleAnimator = null;
        Invalidate();
    }

    public void StartAnimating()
    {
        if (_animating || _row is null || _isListScrolling) return;
        _animating = true;
        PostInvalidateOnAnimation();
    }

    public void StopAnimating() => _animating = false;

    public void SetListScrolling(bool isScrolling)
    {
        if (_isListScrolling == isScrolling) return;
        _isListScrolling = isScrolling;
        if (isScrolling)
        {
            StopAnimating();
            return;
        }

        if (_brandIcon is null) PrepareBrandIcon();
        StartAnimating();
        Invalidate();
    }

    public void RefreshBrandIcon(string brandId)
    {
        if (_row?.Account.Brand.Id is not { } currentId
            || !string.Equals(currentId, brandId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PrepareBrandIcon();
        Invalidate();
    }

    public void CloseActions() => SettleTo(0);

    public void PulseHighlight()
    {
        StopHighlight();
        var animator = ValueAnimator.OfFloat(0, 1, 0, 1, 0, 1, 0, 1, 0)
            ?? throw new InvalidOperationException("Android did not create the highlight animation.");
        _highlightAnimator = animator;
        animator.SetDuration(1000);
        animator.Update += HighlightAnimationUpdated;
        animator.AnimationEnd += HighlightAnimationEnded;
        animator.Start();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_row is null) return;

        SynchronizeDynamicValues();
        var palette = NativeAccountPalette.Current;
        var save = canvas.Save();
        canvas.ClipPath(_cardClipPath);
        DrawActions(canvas, palette);
        canvas.Translate(_swipeOffset, 0);
        DrawForeground(canvas, palette);
        canvas.RestoreToCount(save);
        DrawCardOutline(canvas, palette);

        if (_animating && IsShown) PostInvalidateOnAnimation();
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        _cardBounds.Set(Dp(2), Dp(4), width - Dp(2), height - Dp(4));
        _cardClipPath.Reset();
        _cardClipPath.AddRoundRect(
            _cardBounds,
            Dp(9),
            Dp(9),
            global::Android.Graphics.Path.Direction.Cw!);
        var brandSize = Dp(44);
        var brandLeft = _cardBounds.Left + Dp(10);
        _brandBounds.Set(
            brandLeft,
            _cardBounds.CenterY() - brandSize / 2,
            brandLeft + brandSize,
            _cardBounds.CenterY() + brandSize / 2);
        PrepareBrandIcon();
        UpdateTextLayout();
        UpdateStarPath();
        PrepareEditPath();
    }

    public override bool OnTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null || _row is null || !_viewModel.IsNativeAccountListVisible)
            return false;

        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                _settleAnimator?.Cancel();
                _downX = motionEvent.GetX();
                _downY = motionEvent.GetY();
                _startOffset = _swipeOffset;
                _horizontalGesture = false;
                Pressed = true;
                return true;

            case MotionEventActions.Move:
                var deltaX = motionEvent.GetX() - _downX;
                var deltaY = motionEvent.GetY() - _downY;
                if (!_horizontalGesture)
                {
                    if (Math.Abs(deltaY) > _touchSlop && Math.Abs(deltaY) > Math.Abs(deltaX))
                        return true;
                    if (Math.Abs(deltaX) <= _touchSlop
                        || Math.Abs(deltaX) <= Math.Abs(deltaY) * 1.15f)
                    {
                        return true;
                    }

                    _horizontalGesture = true;
                    Pressed = false;
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                    if (_startOffset <= 0) _openRowChanged(this);
                }

                _swipeOffset = Math.Clamp(
                    _startOffset + deltaX,
                    -Dp(76),
                    Dp(120));
                Invalidate();
                return true;

            case MotionEventActions.Up:
                Pressed = false;
                Parent?.RequestDisallowInterceptTouchEvent(false);
                if (_horizontalGesture)
                {
                    CompleteSwipe();
                    return true;
                }

                HandleTap(motionEvent.GetX(), motionEvent.GetY());
                return true;

            case MotionEventActions.Cancel:
                Pressed = false;
                Parent?.RequestDisallowInterceptTouchEvent(false);
                SettleTo(_startOffset > Dp(60) ? Dp(120) : 0);
                return true;

            default:
                return base.OnTouchEvent(motionEvent);
        }
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null || _row is null) return;

        info.ClassName = "android.widget.Button";
        info.Clickable = true;
        info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(
            FavoriteAccessibilityAction,
            _row.Account.FavoriteActionText));
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(
            QrAccessibilityAction,
            _viewModel.ShowQrText));
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(
            EditAccessibilityAction,
            _viewModel.EditAccountText));
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(
            DeleteAccessibilityAction,
            _viewModel.DeleteAccountText));
    }

    public override bool PerformAccessibilityAction(
        global::Android.Views.Accessibility.Action action,
        Bundle? arguments)
    {
        if (_row is null) return base.PerformAccessibilityAction(action, arguments);
        switch (action)
        {
            case global::Android.Views.Accessibility.Action.Click:
                Invoke(() => _viewModel.CopyAccountCodeAsync(_row.Account));
                return true;
            case (global::Android.Views.Accessibility.Action)FavoriteAccessibilityAction:
                Invoke(() => _viewModel.ToggleAccountFavoriteAsync(_row.Account));
                return true;
            case (global::Android.Views.Accessibility.Action)QrAccessibilityAction:
                Invoke(() => _viewModel.ShowQrForAccountAsync(_row.Account));
                return true;
            case (global::Android.Views.Accessibility.Action)EditAccessibilityAction:
                Invoke(() => _viewModel.BeginEditForAccountAsync(_row.Account));
                return true;
            case (global::Android.Views.Accessibility.Action)DeleteAccessibilityAction:
                Invoke(() => _viewModel.BeginDeleteForAccountAsync(_row.Account));
                return true;
            default:
                return base.PerformAccessibilityAction(action, arguments);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _cardBounds.Dispose();
            _brandBounds.Dispose();
            _cardClipPath.Dispose();
            _starPath.Dispose();
            _editPath.Dispose();
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawActions(Canvas canvas, NativeAccountPalette palette)
    {
        if (Math.Abs(_swipeOffset) < 0.5f) return;

        var card = CardBounds;
        var save = canvas.Save();
        if (_swipeOffset > 0)
        {
            canvas.ClipRect(card.Left, card.Top, Math.Min(card.Right, card.Left + _swipeOffset), card.Bottom);
            _fill.Color = palette.Accent;
            canvas.DrawRoundRect(card, Dp(9), Dp(9), _fill);
            _icon.Color = Color.White;
            _icon.StrokeWidth = Dp(2);
            DrawQrIcon(canvas, Dp(30), Height / 2f, Dp(18));
            DrawEditIcon(canvas);
        }
        else
        {
            canvas.ClipRect(Math.Max(card.Left, card.Right + _swipeOffset), card.Top, card.Right, card.Bottom);
            _fill.Color = palette.Danger;
            canvas.DrawRoundRect(card, Dp(9), Dp(9), _fill);
            _icon.Color = Color.White;
            _icon.StrokeWidth = Dp(2);
            DrawDeleteIcon(canvas, Width - Dp(38), Height / 2f, Dp(18));
        }
        canvas.RestoreToCount(save);
    }

    private void DrawForeground(Canvas canvas, NativeAccountPalette palette)
    {
        var account = _row!.Account;
        var card = CardBounds;
        _fill.Color = palette.Card;
        canvas.DrawRect(card, _fill);

        var left = card.Left + Dp(10);
        if (account.ShowIssuerLogo)
        {
            DrawBrand(canvas, account, card.CenterY());
            left += Dp(54);
        }

        DrawStar(
            canvas,
            account.IsFavorite ? palette.FavoriteFill : palette.Secondary,
            account.IsFavorite ? palette.FavoriteOutline : Color.Transparent);

        var codeRight = card.Right - Dp(10);
        _text.Color = palette.Primary;
        _text.TextSize = ResponsiveSp(17);
        _text.SetTypeface(Typeface.DefaultBold);
        canvas.DrawText(_displayIssuer, left, card.Top + Dp(28), _text);

        _text.SetTypeface(Typeface.Default);
        _text.TextSize = ResponsiveSp(14);
        _text.Color = palette.AccountName;
        canvas.DrawText(_displaySecondary, left, card.Top + Dp(51), _text);

        _text.SetTypeface(Typeface.Monospace);
        _text.TextAlign = Paint.Align.Right;
        SetFittedTextSize(_displayCode, 24, 18, CodeAvailableWidth);
        _text.Color = palette.Code;
        canvas.DrawText(_displayCode, codeRight, card.Top + Dp(35), _text);

        if (account.HasVisibleNextCode)
        {
            _text.SetTypeface(Typeface.Monospace);
            SetFittedTextSize(_displayNextCode, 16, 12, CodeAvailableWidth);
            _text.Color = palette.Secondary;
            canvas.DrawText(_displayNextCode, codeRight, card.Top + Dp(55), _text);
        }
        else
        {
            _text.SetTypeface(Typeface.DefaultBold);
            SetFittedTextSize(account.CopyConfirmation, 10, 8, CodeAvailableWidth);
            _text.Color = palette.CopyConfirmation;
            canvas.DrawText(account.CopyConfirmation, codeRight, card.Top + Dp(54), _text);
        }
        _text.TextAlign = Paint.Align.Left;

        DrawProgress(canvas, account, left, codeRight, card.Bottom - Dp(7), palette);
    }

    private void DrawCardOutline(Canvas canvas, NativeAccountPalette palette)
    {
        var account = _row!.Account;
        var highlighted = _highlightStrength >= 0.45f;
        _stroke.Color = highlighted
            ? palette.ImportHighlight
            : _isSelected
                ? palette.Accent
                : palette.Border;
        _stroke.StrokeWidth = highlighted
            ? Dp(4)
            : _isSelected ? Dp(1.5f) : Dp(1);
        canvas.DrawRoundRect(CardBounds, Dp(9), Dp(9), _stroke);
    }

    private void HighlightAnimationUpdated(object? sender, ValueAnimator.AnimatorUpdateEventArgs args)
    {
        if (args.Animation.AnimatedValue is not Java.Lang.Float value) return;
        _highlightStrength = value.FloatValue();
        Invalidate();
    }

    private void HighlightAnimationEnded(object? sender, EventArgs args) => StopHighlight();

    private void StopHighlight()
    {
        if (_highlightAnimator is not null)
        {
            _highlightAnimator.Update -= HighlightAnimationUpdated;
            _highlightAnimator.AnimationEnd -= HighlightAnimationEnded;
            _highlightAnimator.Cancel();
            _highlightAnimator.Dispose();
            _highlightAnimator = null;
        }

        _highlightStrength = 0;
        Invalidate();
    }

    private void DrawBrand(
        Canvas canvas,
        MobileAccountItem account,
        float centerY)
    {
        var bounds = _brandBounds;
        RefreshThemeAppearance(account.Brand);

        if (_brandIcon is not null)
        {
            if (_drawBrandBackground)
            {
                _fill.Color = _brandColor;
                canvas.DrawRoundRect(bounds, Dp(10), Dp(10), _fill);
            }
            _brandIcon.Draw(canvas, _fill, _stroke);
            return;
        }

        _fill.Color = _drawBrandBackground
            ? _brandColor
            : NativeAccountPalette.Current.GenericBrand;
        canvas.DrawRoundRect(bounds, Dp(10), Dp(10), _fill);

        _text.SetTypeface(Typeface.DefaultBold);
        _text.TextAlign = Paint.Align.Center;
        _text.TextSize = ResponsiveSp(15);
        _text.Color = Color.White;
        var baseline = centerY - (_text.Ascent() + _text.Descent()) / 2;
        canvas.DrawText(account.Brand.Initials, bounds.CenterX(), baseline, _text);
        _text.TextAlign = Paint.Align.Left;
    }

    private void DrawProgress(
        Canvas canvas,
        MobileAccountItem account,
        float left,
        float right,
        float y,
        NativeAccountPalette palette)
    {
        var width = Math.Max(0, right - left);
        _stroke.StrokeWidth = Dp(2);
        _stroke.StrokeCap = Paint.Cap.Round;
        _stroke.Color = palette.ProgressTrack;
        canvas.DrawLine(left, y, right, y, _stroke);

        var periodMilliseconds = Math.Max(1L, account.PeriodSeconds) * 1000L;
        var elapsedInPeriod = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % periodMilliseconds;
        var remainingMilliseconds = periodMilliseconds - elapsedInPeriod;
        _stroke.Color = remainingMilliseconds <= 10_000L ? palette.Danger : palette.Accent;
        float fraction;
        if (account.IsCodeLoading)
        {
            var phase = (SystemClock.ElapsedRealtime() % 900L) / 900f;
            var segment = width * 0.28f;
            var start = left + (width + segment) * phase - segment;
            canvas.DrawLine(Math.Max(left, start), y, Math.Min(right, start + segment), y, _stroke);
            return;
        }

        fraction = remainingMilliseconds / (float)periodMilliseconds;
        canvas.DrawLine(left, y, left + width * Math.Clamp(fraction, 0, 1), y, _stroke);
    }

    private void CompleteSwipe()
    {
        if (_swipeOffset <= -Dp(58))
        {
            SettleTo(0);
            Invoke(() => _viewModel.BeginDeleteForAccountAsync(_row?.Account));
            return;
        }

        SettleTo(_swipeOffset >= Dp(46) ? Dp(120) : 0);
    }

    private void HandleTap(float x, float y)
    {
        var account = _row?.Account;
        if (account is null) return;

        if (IsActionsOpen)
        {
            if (x <= Dp(60))
                Invoke(() => _viewModel.ShowQrForAccountAsync(account));
            else if (x <= Dp(120))
                Invoke(() => _viewModel.BeginEditForAccountAsync(account));
            CloseActions();
            return;
        }

        var starLeft = GetStarLeft();
        if (x >= starLeft && x <= starLeft + Dp(46))
        {
            Invoke(() => _viewModel.ToggleAccountFavoriteAsync(account));
            return;
        }

        Invoke(() => _viewModel.CopyAccountCodeAsync(account));
    }

    private void SettleTo(float target)
    {
        _settleAnimator?.Cancel();
        _settleAnimator?.Dispose();
        var animator = ValueAnimator.OfFloat(_swipeOffset, target)
            ?? throw new InvalidOperationException("Android did not create the row animation.");
        _settleAnimator = animator;
        animator.SetDuration(150);
        animator.Update += SettleAnimationUpdated;
        animator.AnimationEnd += SettleAnimationEnded;
        animator.Start();
    }

    private void SettleAnimationUpdated(object? sender, ValueAnimator.AnimatorUpdateEventArgs args)
    {
        if (args.Animation.AnimatedValue is Java.Lang.Float value)
        {
            _swipeOffset = value.FloatValue();
            Invalidate();
        }
    }

    private void SettleAnimationEnded(object? sender, EventArgs args)
    {
        if (_settleAnimator is not null)
        {
            _settleAnimator.Update -= SettleAnimationUpdated;
            _settleAnimator.AnimationEnd -= SettleAnimationEnded;
            _settleAnimator.Dispose();
            _settleAnimator = null;
        }
        _openRowChanged(this);
    }

    private void AccountPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
#if DEBUG
        if (args.PropertyName == nameof(MobileAccountItem.Code) && _row is not null)
        {
            var account = _row.Account;
            global::Android.Util.Log.Debug(
                "OtpHarborRefresh",
                $"code-notify editor={_viewModel.IsEditorVisible} remaining={account.RemainingSeconds} period={account.PeriodSeconds}");
        }
#endif
        if (args.PropertyName is nameof(MobileAccountItem.Code)
            or nameof(MobileAccountItem.DisplayCode)
            or nameof(MobileAccountItem.DisplayNextCode)
            or nameof(MobileAccountItem.HasVisibleNextCode))
        {
            _rawCode = _row?.Account.Code ?? string.Empty;
            _displayCode = _row?.Account.DisplayCode ?? string.Empty;
            _displayNextCode = _row?.Account.DisplayNextCode ?? string.Empty;
        }
        if (args.PropertyName is nameof(MobileAccountItem.Brand)
            or nameof(MobileAccountItem.ShowIssuerLogo))
        {
            if (_row is not null)
            {
                UpdateBrandAppearance(_row.Account.Brand);
            }
            PrepareBrandIcon();
            UpdateTextLayout();
            UpdateStarPath();
        }
        else if (args.PropertyName is nameof(MobileAccountItem.Issuer)
            or nameof(MobileAccountItem.AccountName)
            or nameof(MobileAccountItem.ConfiguredPeriodSeconds)
            or nameof(MobileAccountItem.CustomPeriodLabel))
        {
            UpdateTextLayout();
            UpdateAccessibilityDescription();
        }
        PostInvalidate();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MobileShellViewModel.SelectedAccount) || _row is null) return;
        var isSelected = _viewModel.SelectedAccount?.Id == _row.Id;
        if (_isSelected == isSelected) return;
        _isSelected = isSelected;
        PostInvalidate();
    }

    private void UpdateAccessibilityDescription()
    {
        if (_row is null) return;
        ContentDescription = _row.Account.DisplayName;
    }

    private void SynchronizeDynamicValues()
    {
        var account = _row?.Account;
        if (account is null) return;

        if (!string.Equals(_rawCode, account.Code, StringComparison.Ordinal))
        {
            _rawCode = account.Code;
            _displayCode = account.DisplayCode;
            _displayNextCode = account.DisplayNextCode;
        }
    }

    private void Unsubscribe()
    {
        if (_row is not null) _row.Account.PropertyChanged -= AccountPropertyChanged;
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
    }

    private void Invoke(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = action();
            return;
        }

        Dispatcher.UIThread.Post(() => _ = action());
    }

    private RectF CardBounds => _cardBounds;

    private int Dp(float value) => (int)MathF.Round(value * _density);
    private float ResponsiveSp(float value) => NativeTextSizing.ResolvePixels(
        value,
        _density,
        _fontScale,
        Width > 0 ? Width / _density : 344f,
        344f);

    private float CodeAvailableWidth => Math.Max(
        0,
        _cardBounds.Right - Dp(10) - (GetStarLeft() + Dp(46)));

    private void SetFittedTextSize(
        string value,
        float preferredSp,
        float minimumSp,
        float availableWidth)
    {
        var preferredPixels = ResponsiveSp(preferredSp);
        _text.TextSize = preferredPixels;
        _text.TextSize = NativeTextSizing.FitPixels(
            preferredPixels,
            _text.MeasureText(value),
            availableWidth,
            ResponsiveSp(minimumSp));
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

    private void UpdateTextLayout()
    {
        var account = _row?.Account;
        if (account is null || Width <= 0)
        {
            _displayIssuer = string.Empty;
            _displaySecondary = string.Empty;
            return;
        }

        var left = _cardBounds.Left + Dp(10)
            + (account.ShowIssuerLogo ? Dp(54) : 0);
        var availableWidth = Math.Max(0, GetStarLeft() - Dp(8) - left);

        _text.SetTypeface(Typeface.DefaultBold);
        _text.TextSize = ResponsiveSp(17);
        _displayIssuer = Ellipsize(account.Issuer, availableWidth);

        var secondary = account.AccountName;
        if (account.HasCustomPeriod && account.CustomPeriodLabel.Length > 0)
        {
            secondary = secondary.Length == 0
                ? account.CustomPeriodLabel
                : $"{secondary} · {account.CustomPeriodLabel}";
        }
        _text.SetTypeface(Typeface.Default);
        _text.TextSize = ResponsiveSp(14);
        _displaySecondary = Ellipsize(secondary, availableWidth);
    }

    private void PrepareBrandIcon(bool requireRuntimeIconOnFirstDraw = true)
    {
        _brandIcon?.Dispose();
        _brandIcon = null;
        var account = _row?.Account;
        if (account is null
            || !account.ShowIssuerLogo
            || Height <= 0)
        {
            return;
        }

        var appearance = account.Brand.RuntimeAppearanceFor(
            global::Avalonia.Application.Current?.ActualThemeVariant);
        var inset = appearance is null ? Dp(9) : 0;
        using var target = new RectF(
            _brandBounds.Left + inset,
            _brandBounds.Top + inset,
            _brandBounds.Right - inset,
            _brandBounds.Bottom - inset);
        BrandInfo brand;
        if (account.Brand.HasRuntimeIcon)
        {
            brand = account.Brand;
        }
        else if (_isListScrolling)
        {
            if (!_viewModel.TryGetMaterializedBrand(account.Brand, out brand)) return;
        }
        else
        {
            brand = _viewModel.MaterializeBrand(account.Brand);
        }

        _brandIcon = _brandIcons.TryCreate(
            brand,
            target,
            Color.White,
            allowTemplateCreation: !_isListScrolling,
            requireRuntimeIconOnFirstDraw);
        _preparedRuntimeCacheKey = brand.RuntimeIconFor(
            global::Avalonia.Application.Current?.ActualThemeVariant)?.CacheKey;
    }

    private void RefreshThemeAppearance(BrandInfo brand)
    {
        var cacheKey = brand.RuntimeIconFor(
            global::Avalonia.Application.Current?.ActualThemeVariant)?.CacheKey;
        if (string.Equals(cacheKey, _preparedRuntimeCacheKey, StringComparison.Ordinal)) return;
        UpdateBrandAppearance(brand);
        PrepareBrandIcon(requireRuntimeIconOnFirstDraw: false);
    }

    private void UpdateBrandAppearance(BrandInfo brand)
    {
        var appearance = brand.RuntimeAppearanceFor(
            global::Avalonia.Application.Current?.ActualThemeVariant);
        _drawBrandBackground = appearance?.BackgroundMode != BrandIconBackgroundMode.Embedded;
        _brandColor = appearance is { BackgroundMode: BrandIconBackgroundMode.Solid,
                                      BackgroundColor: { } color }
            ? TryColor(color, NativeAccountPalette.Current.GenericBrand)
            : TryColor(brand.BackgroundColor, NativeAccountPalette.Current.GenericBrand);
    }

    private void UpdateStarPath()
    {
        _starPath.Reset();
        var account = _row?.Account;
        if (account is null || Width <= 0) return;
        var centerX = GetStarLeft() + Dp(23);
        var centerY = _cardBounds.CenterY();
        var radius = Dp(12);
        for (var index = 0; index < 10; index++)
        {
            var angle = -MathF.PI / 2 + index * MathF.PI / 5;
            var pointRadius = index % 2 == 0 ? radius : radius * 0.45f;
            var x = centerX + MathF.Cos(angle) * pointRadius;
            var y = centerY + MathF.Sin(angle) * pointRadius;
            if (index == 0) _starPath.MoveTo(x, y); else _starPath.LineTo(x, y);
        }
        _starPath.Close();
    }

    private float GetStarLeft()
    {
        var codeWidth = Math.Min(Dp(126), Math.Max(Dp(92), Width * 0.31f));
        return _cardBounds.Right - Dp(10) - codeWidth - Dp(46);
    }

    private void PrepareEditPath()
    {
        _editPath.Reset();
        if (Height <= 0) return;

        using var sourcePath = PathParser.CreatePathFromPathData(EditIconData);
        if (sourcePath is null) return;
        using var sourceBounds = new RectF();
        sourcePath.ComputeBounds(sourceBounds, true);
        using var target = new RectF(
            Dp(90) - Dp(9),
            Height / 2f - Dp(9),
            Dp(90) + Dp(9),
            Height / 2f + Dp(9));
        using var matrix = new Matrix();
        matrix.SetRectToRect(sourceBounds, target, Matrix.ScaleToFit.Center);
        sourcePath.Transform(matrix, _editPath);
    }

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

    private void DrawStar(Canvas canvas, Color color, Color outlineColor)
    {
        _fill.Color = color;
        _fill.Alpha = _row?.Account.IsFavorite == true ? 255 : 145;
        canvas.DrawPath(_starPath, _fill);
        _fill.Alpha = 255;
        _stroke.Color = outlineColor;
        _stroke.StrokeWidth = Dp(1.6f);
        canvas.DrawPath(_starPath, _stroke);
    }

    private void DrawQrIcon(Canvas canvas, float centerX, float centerY, float size)
    {
        var cell = size / 3f;
        var left = centerX - size / 2;
        var top = centerY - size / 2;
        canvas.DrawRect(left, top, left + cell, top + cell, _icon);
        canvas.DrawRect(left + size - cell, top, left + size, top + cell, _icon);
        canvas.DrawRect(left, top + size - cell, left + cell, top + size, _icon);
        canvas.DrawRect(left + cell * 1.5f, top + cell * 1.5f, left + size, top + size, _icon);
    }

    private void DrawEditIcon(Canvas canvas)
    {
        _fill.Color = Color.White;
        canvas.DrawPath(_editPath, _fill);
    }

    private void DrawDeleteIcon(Canvas canvas, float centerX, float centerY, float size)
    {
        var half = size / 2;
        canvas.DrawRect(centerX - half * 0.7f, centerY - half * 0.45f,
            centerX + half * 0.7f, centerY + half, _icon);
        canvas.DrawLine(centerX - half, centerY - half * 0.65f,
            centerX + half, centerY - half * 0.65f, _icon);
        canvas.DrawLine(centerX - half * 0.35f, centerY - half,
            centerX + half * 0.35f, centerY - half, _icon);
    }
}

internal sealed record NativeAccountPalette(
    Color Window,
    Color Card,
    Color Border,
    Color Primary,
    Color Secondary,
    Color AccountName,
    Color Code,
    Color Accent,
    Color ImportHighlight,
    Color CopyConfirmation,
    Color FavoriteFill,
    Color FavoriteOutline,
    Color Danger,
    Color ProgressTrack,
    Color GenericBrand)
{
    private static readonly NativeAccountPalette Dark = new(
        Color.ParseColor("#0C1C33"),
        Color.ParseColor("#10213F"),
        Color.ParseColor("#2D466A"),
        Color.White,
        Color.ParseColor("#CBDAF3"),
        Color.ParseColor("#CBDAF3"),
        Color.ParseColor("#97B0DF"),
        Color.ParseColor("#7D7FF4"),
        Color.ParseColor("#FFD166"),
        Color.ParseColor("#B7FF4A"),
        Color.Transparent,
        Color.ParseColor("#FFD166"),
        Color.ParseColor("#EF5350"),
        Color.ParseColor("#526A8C"),
        Color.ParseColor("#334155"));

    private static readonly NativeAccountPalette Light = new(
        Color.White,
        Color.ParseColor("#F2F4FB"),
        Color.ParseColor("#D6DEE8"),
        Color.ParseColor("#172033"),
        Color.ParseColor("#526174"),
        Color.ParseColor("#7E7E84"),
        Color.ParseColor("#185F99"),
        Color.ParseColor("#168AE0"),
        Color.ParseColor("#D89A12"),
        Color.ParseColor("#168AE0"),
        Color.ParseColor("#168AE0"),
        Color.Transparent,
        Color.ParseColor("#B42318"),
        Color.ParseColor("#CFD8E5"),
        Color.ParseColor("#334155"));

    private static readonly NativeAccountPalette HighContrast = new(
        Color.Black,
        Color.Black,
        Color.White,
        Color.White,
        Color.White,
        Color.White,
        Color.White,
        Color.Yellow,
        Color.Yellow,
        Color.Yellow,
        Color.Yellow,
        Color.Transparent,
        Color.ParseColor("#FF8080"),
        Color.White,
        Color.ParseColor("#334155"));

    public static NativeAccountPalette Current
    {
        get
        {
            var theme = global::Avalonia.Application.Current?.ActualThemeVariant;
            if (theme == AvaloniaThemeVariants.HighContrast) return HighContrast;
            return theme == ThemeVariant.Dark ? Dark : Light;
        }
    }
}
