using System.ComponentModel;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Avalonia.Styling;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Shared.Styles;

namespace TOTP.Avalonia.Android;

internal sealed class NativeDeleteConfirmationDialog : IDisposable
{
    private readonly View _owner;
    private readonly MobileShellViewModel _viewModel;
    private readonly ActionClickListener _confirmClick;
    private readonly ActionClickListener _cancelClick;
    private readonly DialogCancelListener _dialogCancel;
    private AlertDialog? _dialog;
    private TextView? _title;
    private TextView? _message;
    private Button? _confirmButton;
    private Button? _cancelButton;
    private bool _disposed;

    public NativeDeleteConfirmationDialog(View owner, MobileShellViewModel viewModel)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _confirmClick = new ActionClickListener(() => _ = ConfirmAsync());
        _cancelClick = new ActionClickListener(Cancel);
        _dialogCancel = new DialogCancelListener(Cancel);
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        Update();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        Dismiss();
        _confirmClick.Dispose();
        _cancelClick.Dispose();
        _dialogCancel.Dispose();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MobileShellViewModel.IsDeleteConfirmationVisible)
            or nameof(MobileShellViewModel.DeleteConfirmTitle)
            or nameof(MobileShellViewModel.DeletePrompt)
            or nameof(MobileShellViewModel.DeleteConfirmationActionText)
            or nameof(MobileShellViewModel.CancelText))
        {
            _owner.Post(Update);
        }
    }

    private void Update()
    {
        if (_disposed) return;
        if (!_viewModel.IsDeleteConfirmationVisible)
        {
            Dismiss();
            return;
        }

        if (_dialog is null)
            Show();
        else
            UpdateContent();
    }

    private void Show()
    {
        using var builder = new AlertDialog.Builder(_owner.Context);
        builder.SetView(CreateContent());

        var dialog = builder.Create()
            ?? throw new InvalidOperationException(
                "Android did not create the account deletion dialog.");
        dialog.SetCancelable(true);
        dialog.SetCanceledOnTouchOutside(false);
        dialog.SetOnCancelListener(_dialogCancel);
        dialog.Show();
        ConfigureWindow(dialog);

        _dialog = dialog;
        UpdateContent();
    }

    private View CreateContent()
    {
        var context = _owner.Context;
        var palette = DeleteDialogPalette.Current;
        var content = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical,
            Background = CreateRoundedBackground(
                palette.Surface,
                palette.Border,
                12,
                1),
            Elevation = Dp(12)
        };
        content.SetPadding(Dp(22), Dp(22), Dp(22), Dp(22));

        _title = new TextView(context)
        {
            Text = _viewModel.DeleteConfirmTitle,
            Gravity = GravityFlags.Start
        };
        _title.SetTextColor(palette.Primary);
        _title.SetTextSize(ComplexUnitType.Sp, 20);
        _title.SetTypeface(
            Typeface.Create("sans-serif-medium", TypefaceStyle.Normal)
                ?? Typeface.DefaultBold,
            TypefaceStyle.Normal);
        content.AddView(_title, WithBottomMargin(16));

        _message = new TextView(context)
        {
            Text = _viewModel.DeletePrompt,
            Gravity = GravityFlags.Start
        };
        _message.SetTextColor(palette.Primary);
        _message.SetTextSize(ComplexUnitType.Sp, 16);
        _message.SetLineSpacing(0, 1.12f);
        content.AddView(_message, WithBottomMargin(16));

        var actions = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            WeightSum = 2
        };
        _cancelButton = CreateButton(
            _viewModel.CancelText,
            palette.Primary,
            palette.Surface,
            palette.SurfacePressed,
            palette.Border,
            _cancelClick);
        _confirmButton = CreateButton(
            _viewModel.DeleteConfirmationActionText,
            palette.OnDanger,
            palette.Danger,
            palette.DangerPressed,
            palette.Danger,
            _confirmClick);

        var cancelLayout = new LinearLayout.LayoutParams(0, Dp(52), 1);
        cancelLayout.SetMargins(0, 0, Dp(5), 0);
        actions.AddView(_cancelButton, cancelLayout);
        var confirmLayout = new LinearLayout.LayoutParams(0, Dp(52), 1);
        confirmLayout.SetMargins(Dp(5), 0, 0, 0);
        actions.AddView(_confirmButton, confirmLayout);
        content.AddView(actions, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));
        return content;
    }

    private Button CreateButton(
        string text,
        Color foreground,
        Color background,
        Color pressedBackground,
        Color border,
        View.IOnClickListener listener)
    {
        var button = new Button(_owner.Context)
        {
            Text = text,
            Gravity = GravityFlags.Center,
            Background = CreateButtonBackground(background, pressedBackground, border)
        };
        button.SetAllCaps(false);
        button.SetPadding(Dp(10), 0, Dp(10), 0);
        button.SetTextColor(foreground);
        button.SetTextSize(ComplexUnitType.Sp, 16);
        button.SetSingleLine(false);
        button.SetMaxLines(2);
        button.SetOnClickListener(listener);
        button.ContentDescription = text;
        return button;
    }

    private void ConfigureWindow(AlertDialog dialog)
    {
        var window = dialog.Window;
        if (window is null) return;

        window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        window.AddFlags(WindowManagerFlags.DimBehind);
        var attributes = window.Attributes;
        if (attributes is not null)
        {
            attributes.DimAmount = 0.6f;
            window.Attributes = attributes;
        }

        var screenWidth = _owner.Resources?.DisplayMetrics?.WidthPixels ?? Dp(468);
        var width = Math.Min(Dp(420), Math.Max(1, screenWidth - Dp(48)));
        window.SetLayout(width, ViewGroup.LayoutParams.WrapContent);
    }

    private LinearLayout.LayoutParams WithBottomMargin(int marginDp)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, 0, 0, Dp(marginDp));
        return layout;
    }

    private StateListDrawable CreateButtonBackground(
        Color normal,
        Color pressed,
        Color border)
    {
        var states = new StateListDrawable();
        states.AddState(
            [global::Android.Resource.Attribute.StatePressed],
            CreateRoundedBackground(pressed, border, 6, 1));
        states.AddState([], CreateRoundedBackground(normal, border, 6, 1));
        return states;
    }

    private GradientDrawable CreateRoundedBackground(
        Color fill,
        Color border,
        int radiusDp,
        int borderDp)
    {
        var background = new GradientDrawable();
        background.SetColor(fill);
        background.SetCornerRadius(Dp(radiusDp));
        background.SetStroke(Dp(borderDp), border);
        return background;
    }

    private int Dp(int value) => (int)Math.Round(
        value * (_owner.Resources?.DisplayMetrics?.Density ?? 1));

    private void UpdateContent()
    {
        if (_dialog is null) return;
        if (_title is not null)
            _title.Text = _viewModel.DeleteConfirmTitle;
        if (_message is not null)
            _message.Text = _viewModel.DeletePrompt;
        if (_cancelButton is not null)
        {
            _cancelButton.Text = _viewModel.CancelText;
            _cancelButton.ContentDescription = _viewModel.CancelText;
        }
        if (_confirmButton is not null)
        {
            _confirmButton.Text = _viewModel.DeleteConfirmationActionText;
            _confirmButton.ContentDescription = _viewModel.DeleteConfirmationActionText;
        }
    }

    private async Task ConfirmAsync()
    {
        if (_disposed || _confirmButton?.Enabled == false) return;
        if (_confirmButton is not null) _confirmButton.Enabled = false;
        if (_cancelButton is not null) _cancelButton.Enabled = false;
        try
        {
            await _viewModel.ConfirmDeleteAsync();
        }
        finally
        {
            if (_viewModel.IsDeleteConfirmationVisible)
            {
                UpdateContent();
                if (_confirmButton is not null) _confirmButton.Enabled = true;
                if (_cancelButton is not null) _cancelButton.Enabled = true;
            }
            else
            {
                Dismiss();
            }
        }
    }

    private void Cancel()
    {
        if (_disposed) return;
        _ = _viewModel.CancelDeleteAsync();
        Dismiss();
    }

    private void Dismiss()
    {
        _title = null;
        _message = null;
        _confirmButton = null;
        _cancelButton = null;
        if (_dialog is null) return;
        _dialog.SetOnCancelListener(null);
        _dialog.Dismiss();
        _dialog.Dispose();
        _dialog = null;
    }

    private sealed class ActionClickListener(Action action) : Java.Lang.Object, View.IOnClickListener
    {
        public void OnClick(View? view) => action();
    }

    private sealed class DialogCancelListener(Action action)
        : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDialogInterface? dialog) => action();
    }

    private sealed record DeleteDialogPalette(
        Color Surface,
        Color SurfacePressed,
        Color Border,
        Color Primary,
        Color Danger,
        Color DangerPressed,
        Color OnDanger)
    {
        private static readonly DeleteDialogPalette Light = new(
            Color.ParseColor("#FFFFFF"),
            Color.ParseColor("#CFDFEC"),
            Color.ParseColor("#D7E1EB"),
            Color.ParseColor("#172033"),
            Color.ParseColor("#B42318"),
            Color.ParseColor("#8F1C13"),
            Color.White);

        private static readonly DeleteDialogPalette Dark = new(
            Color.ParseColor("#10213F"),
            Color.ParseColor("#152744"),
            Color.ParseColor("#1D3366"),
            Color.White,
            Color.ParseColor("#EF5350"),
            Color.ParseColor("#D84340"),
            Color.White);

        private static readonly DeleteDialogPalette HighContrast = new(
            Color.Black,
            Color.Black,
            Color.White,
            Color.White,
            Color.ParseColor("#FF8080"),
            Color.ParseColor("#FF8080"),
            Color.Black);

        public static DeleteDialogPalette Current
        {
            get
            {
                var theme = global::Avalonia.Application.Current?.ActualThemeVariant;
                if (theme == AvaloniaThemeVariants.HighContrast) return HighContrast;
                return theme == ThemeVariant.Dark ? Dark : Light;
            }
        }
    }
}
