using System.Collections.Specialized;
using System.ComponentModel;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Shared.Import;

namespace TOTP.Avalonia.Android;

internal sealed class NativeImportReviewRecyclerView : RecyclerView
{
    private readonly MobileShellViewModel _shell;
    private readonly ReviewAdapter _adapter;
    private ImportReviewViewModel? _review;
    private bool _submitPending;
    private bool _disposed;

    public NativeImportReviewRecyclerView(Context context, MobileShellViewModel shell) : base(context)
    {
        _shell = shell;
        _adapter = new ReviewAdapter(context) { HasStableIds = true };
        SetLayoutManager(new LinearLayoutManager(context, Vertical, false));
        SetItemAnimator(null);
        SetItemViewCacheSize(6);
        GetRecycledViewPool().SetMaxRecycledViews(1, 8);
        HasFixedSize = true;
        VerticalScrollBarEnabled = false;
        OverScrollMode = OverScrollMode.IfContentScrolls;
        DescendantFocusability = DescendantFocusability.AfterDescendants;
        SetAdapter(_adapter);
        _shell.PropertyChanged += ShellChanged;
        if (global::Avalonia.Application.Current is { } application)
            application.ActualThemeVariantChanged += ThemeChanged;
        SetReview(shell.ExternalImportReview);
    }

    private void ShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MobileShellViewModel.ExternalImportReview))
            Post(() => { if (!_disposed) SetReview(_shell.ExternalImportReview); });
    }

    private void SetReview(ImportReviewViewModel? review)
    {
        if (ReferenceEquals(_review, review)) return;
        if (_review is not null)
        {
            _review.MobileItems.CollectionChanged -= ItemsChanged;
            _review.PropertyChanged -= ReviewChanged;
        }
        StopScroll();
        _review = review;
        if (review is not null)
        {
            review.MobileItems.CollectionChanged += ItemsChanged;
            review.PropertyChanged += ReviewChanged;
        }
        SetBackgroundColor(NativeAccountPalette.Current.Window);
        _adapter.Submit(review);
        ScrollToPosition(0);
    }

    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed || _submitPending) return;
        _submitPending = true;
        Post(() =>
        {
            _submitPending = false;
            if (!_disposed) _adapter.Submit(_review);
        });
    }

    private void ReviewChanged(object? sender, PropertyChangedEventArgs args) =>
        Post(() => { if (!_disposed) _adapter.RefreshHeader(); });

    private void ThemeChanged(object? sender, EventArgs args) => Post(() =>
    {
        if (_disposed) return;
        SetBackgroundColor(NativeAccountPalette.Current.Window);
        _adapter.RefreshTheme();
    });

    public void DisposeHost()
    {
        if (_disposed) return;
        _shell.PropertyChanged -= ShellChanged;
        if (global::Avalonia.Application.Current is { } application)
            application.ActualThemeVariantChanged -= ThemeChanged;
        SetReview(null);
        _disposed = true;
        _adapter.Clear();
        SetAdapter(null);
        Dispose();
    }

    private sealed class ReviewAdapter(Context context) : Adapter
    {
        private ImportReviewViewModel? _model;
        private ImportReviewViewModel.ReviewRow[] _rows = [];
        private readonly HashSet<CardHolder> _holders = [];
        private HeaderHolder? _header;

        public override int ItemCount => _model is null ? 0 : _rows.Length + 1;
        public override int GetItemViewType(int position) => position == 0 ? 0 : 1;
        public override long GetItemId(int position) => position == 0 ? long.MinValue : _rows[position - 1].ImportIndex;

        public override ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            if (viewType == 0) return _header = new HeaderHolder(new NativeReviewHeader(context));
            var holder = new CardHolder(new NativeReviewCard(context));
            _holders.Add(holder);
            return holder;
        }

        public override void OnBindViewHolder(ViewHolder holder, int position)
        {
            if (_model is null) return;
            if (holder is HeaderHolder header) header.Header.Bind(_model);
            else ((CardHolder)holder).Card.Bind(_rows[position - 1]);
        }

        public override void OnViewRecycled(Java.Lang.Object holder)
        {
            if (holder is CardHolder card) card.Card.Bind(null);
            base.OnViewRecycled(holder);
        }

        public void Submit(ImportReviewViewModel? model)
        {
            _model = model;
            _rows = model?.VisibleRows.ToArray() ?? [];
            if (model is null) Clear();
            NotifyDataSetChanged();
        }

        public void RefreshHeader() => _header?.Header.Refresh();
        public void RefreshTheme()
        {
            RefreshHeader();
            foreach (var holder in _holders) holder.Card.Refresh();
        }

        public void Clear()
        {
            _header?.Header.Bind(null);
            foreach (var holder in _holders) holder.Card.Bind(null);
            _model = null;
            _rows = [];
        }

        private sealed class HeaderHolder(NativeReviewHeader header) : ViewHolder(header)
        {
            public NativeReviewHeader Header { get; } = header;
        }
        private sealed class CardHolder(NativeReviewCard card) : ViewHolder(card)
        {
            public NativeReviewCard Card { get; } = card;
        }
    }
}

internal abstract class NativeReviewLayout : LinearLayout
{
    protected NativeReviewLayout(Context context) : base(context)
    {
        Orientation = Orientation.Vertical;
    }

    protected int Dp(float value) => (int)MathF.Round(value * (Resources?.DisplayMetrics?.Density ?? 1));
    protected TextView Label(float size = 14, bool bold = false)
    {
        var text = new TextView(Context);
        text.SetTextSize(ComplexUnitType.Sp, size);
        if (bold) text.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        text.SetLineSpacing(0, 1.08f);
        return text;
    }
    protected static void Text(TextView view, string value)
    {
        if (!string.Equals(view.Text, value, StringComparison.Ordinal)) view.Text = value;
    }
    protected GradientDrawable Surface(Color fill, Color border)
    {
        var background = new GradientDrawable();
        background.SetColor(fill);
        background.SetCornerRadius(Dp(5));
        background.SetStroke(Dp(1), border);
        return background;
    }
    protected static Color SemanticColor(string key, Color fallback)
    {
        var application = global::Avalonia.Application.Current;
        if (application?.TryGetResource(key, application.ActualThemeVariant, out var resource) == true
            && resource is global::Avalonia.Media.ISolidColorBrush brush)
            return new Color(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A);
        return fallback;
    }
    protected LayoutParams Row(int bottom = 8) => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
    { BottomMargin = Dp(bottom) };
    protected void Colors(View view, NativeAccountPalette palette)
    {
        if (view is TextView text) text.SetTextColor(palette.Primary);
        if (view is ViewGroup group)
            for (var index = 0; index < group.ChildCount; index++)
                if (group.GetChildAt(index) is { } child) Colors(child, palette);
    }
}

internal sealed class NativeReviewCard : NativeReviewLayout
{
    private readonly LinearLayout _header;
    private readonly TextView _issue, _identity, _parameters, _action, _chevron;
    private readonly LinearLayout _details;
    private readonly TextView _description, _changes, _existingTitle, _existing, _incomingTitle, _incoming, _help;
    private readonly LinearLayout _comparison;
    private readonly RadioGroup _choices;
    private readonly RadioButton[] _options;
    private ImportReviewViewModel.ReviewRow? _model;
    private bool _binding;

    public NativeReviewCard(Context context) : base(context)
    {
        var layout = new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(Dp(16), 0, Dp(16), Dp(8));
        LayoutParameters = layout;
        SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        _header = new LinearLayout(context) { Orientation = Orientation.Horizontal, Focusable = true, Clickable = true };
        _header.SetGravity(GravityFlags.CenterVertical);
        _header.SetMinimumHeight(Dp(48));
        var labels = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _issue = Label(16, true); _identity = Label(); _parameters = Label(12); _action = Label(12);
        foreach (var label in new[] { _issue, _identity, _parameters, _action }) labels.AddView(label, Row(4));
        _chevron = Label(22);
        _chevron.ImportantForAccessibility = ImportantForAccessibility.No;
        _header.AddView(labels, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        _header.AddView(_chevron, new LayoutParams(Dp(32), Dp(44)));
        _header.Click += (_, _) => { if (_model is { } row) row.IsExpanded = !row.IsExpanded; };
        AddView(_header, Row(0));
        _details = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _details.SetPadding(0, Dp(12), 0, 0);
        _description = Label(); _changes = Label(12);
        _details.AddView(_description, Row(10)); _details.AddView(_changes, Row(8));
        _comparison = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _comparison.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
        _existingTitle = Label(14, true); _existing = Label(13); _incomingTitle = Label(14, true); _incoming = Label(13);
        foreach (var label in new[] { _existingTitle, _existing, _incomingTitle, _incoming }) _comparison.AddView(label, Row(6));
        _details.AddView(_comparison, Row(10));
        _choices = new RadioGroup(context) { Orientation = Orientation.Vertical };
        _options = Enumerable.Range(0, 3).Select(_ => new RadioButton(context) { Id = GenerateViewId(), Gravity = GravityFlags.CenterVertical }).ToArray();
        foreach (var option in _options)
        {
            option.SetTextSize(ComplexUnitType.Sp, 16);
            option.SetMinimumHeight(Dp(48));
            option.SetPadding(Dp(8), Dp(4), Dp(8), Dp(4));
            _choices.AddView(option, Row(6));
        }
        _choices.CheckedChange += (_, args) =>
        {
            if (_binding || _model is not { } row) return;
            if (args.CheckedId == _options[0].Id) row.IsReplace = true;
            else if (args.CheckedId == _options[1].Id) row.IsKeepBoth = true;
            else if (args.CheckedId == _options[2].Id) row.IsSkip = true;
        };
        _details.AddView(_choices, Row(6));
        _help = Label(12);
        _details.AddView(_help, Row(0));
        AddView(_details, Row(0));
    }

    public void Bind(ImportReviewViewModel.ReviewRow? model)
    {
        if (!ReferenceEquals(_model, model))
        {
            if (_model is not null) _model.PropertyChanged -= Changed;
            _model = model;
            if (model is not null) model.PropertyChanged += Changed;
        }
        if (model is not null) Refresh();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Post(() =>
    {
        if (ReferenceEquals(sender, _model)) Refresh();
    });

    public void Refresh()
    {
        if (_model is not { } row) return;
        _binding = true;
        try
        {
            var palette = NativeAccountPalette.Current;
            var surface = SemanticColor("BrushSurface", palette.Window);
            var alternate = SemanticColor("BrushSurfaceAlternate", palette.Card);
            Colors(this, palette);
            Background = Surface(surface, palette.Border);
            _comparison.Background = Surface(alternate, alternate);
            Text(_issue, row.IssueText); Text(_identity, row.Identity); Text(_parameters, row.Parameters); Text(_action, row.ActionSummary);
            _parameters.SetTextColor(palette.Secondary); _action.SetTextColor(palette.Accent);
            Text(_chevron, row.IsExpanded ? "⌃" : "⌄");
            _header.ContentDescription = string.Join(Environment.NewLine, row.IssueText, row.Identity, row.Parameters, row.ActionSummary);
            _details.Visibility = row.IsExpanded ? ViewStates.Visible : ViewStates.Gone;
            Text(_description, row.IssueDescription); Text(_changes, row.ChangedFieldsText);
            _description.SetTextColor(palette.Secondary); _changes.SetTextColor(palette.Accent);
            _changes.Visibility = row.IsConflict ? ViewStates.Visible : ViewStates.Gone;
            Text(_existingTitle, row.ExistingText); Text(_existing, row.ExistingParameters);
            Text(_incomingTitle, row.IncomingText); Text(_incoming, row.IncomingParameters);
            _choices.Visibility = row.IsConflict ? ViewStates.Visible : ViewStates.Gone;
            var captions = new[] { row.ReplaceText, row.KeepBothText, row.SkipText };
            var selected = new[] { row.IsReplace, row.IsKeepBoth, row.IsSkip };
            for (var index = 0; index < _options.Length; index++)
            {
                var option = _options[index];
                Text(option, captions[index]);
                option.Checked = selected[index];
                option.Background = Surface(selected[index] ? alternate : surface, selected[index] ? palette.Accent : palette.Border);
                option.ButtonTintList = global::Android.Content.Res.ColorStateList.ValueOf(palette.Accent);
            }
            Text(_help, row.IsConflict ? row.ChoicesHelp : row.ActionSummary);
            _help.SetTextColor(palette.Secondary);
        }
        finally { _binding = false; }
    }
}

internal sealed class NativeReviewHeader : NativeReviewLayout
{
    private readonly TextView _breadcrumb, _title, _steps, _summary, _file, _local, _excluded, _empty;
    private readonly TextView[] _counts, _captions;
    private readonly LinearLayout _summaryCard;
    private readonly EditText _search;
    private readonly Spinner _filter, _bulk;
    private readonly CheckBox _select;
    private ImportReviewViewModel? _model;
    private bool _binding;
    private string[] _filterLabels = [], _bulkLabels = [];

    public NativeReviewHeader(Context context) : base(context)
    {
        LayoutParameters = new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        SetPadding(Dp(16), Dp(16), Dp(16), Dp(8));
        _breadcrumb = Label(12); _title = Label(24, true); _steps = Label(12);
        AddView(_breadcrumb, Row(6)); AddView(_title, Row(12)); AddView(_steps, Row(12));
        _summaryCard = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _summaryCard.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _summary = Label(18, true); _file = Label(12);
        _file.SetMaxLines(2); _file.Ellipsize = TextUtils.TruncateAt.Middle;
        _summaryCard.AddView(_summary, Row(4)); _summaryCard.AddView(_file, Row(12));
        _counts = new TextView[6]; _captions = new TextView[6];
        for (var line = 0; line < 2; line++)
        {
            var metrics = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            for (var column = 0; column < 3; column++)
            {
                var index = line * 3 + column;
                var metric = new LinearLayout(context) { Orientation = Orientation.Vertical };
                metric.SetGravity(GravityFlags.Center);
                _counts[index] = Label(24, true); _counts[index].Gravity = GravityFlags.Center;
                _captions[index] = Label(12); _captions[index].Gravity = GravityFlags.Center;
                metric.AddView(_counts[index], Row(4)); metric.AddView(_captions[index], Row(0));
                metrics.AddView(metric, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            }
            _summaryCard.AddView(metrics, Row(line == 0 ? 12 : 0));
        }
        AddView(_summaryCard, Row(12));
        _local = Label(12); _excluded = Label(12);
        AddView(_local, Row(8)); AddView(_excluded, Row(8));
        _search = new EditText(context) { InputType = InputTypes.ClassText | InputTypes.TextVariationFilter };
        _search.SetSingleLine(true);
        _search.SetTextSize(ComplexUnitType.Sp, 14);
        _search.SetMinimumHeight(Dp(48));
        _search.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        _search.TextChanged += (_, _) =>
        {
            if (!_binding && _model is { } model)
            {
                var value = _search.Text ?? string.Empty;
                Dispatcher.UIThread.Post(() => { if (ReferenceEquals(model, _model)) model.Search = value; });
            }
        };
        AddView(_search, Row(8));
        _filter = new Spinner(context); _filter.SetMinimumHeight(Dp(48));
        _filter.ItemSelected += (_, args) =>
        {
            if (!_binding && _model is { } model && _filter.SelectedItemPosition == args.Position && args.Position >= 0 && args.Position < model.Categories.Count
                && !ReferenceEquals(model.SelectedCategory, model.Categories[args.Position]))
                Dispatcher.UIThread.Post(() => { if (ReferenceEquals(model, _model)) model.SelectedCategory = model.Categories[args.Position]; });
        };
        AddView(_filter, Row(8));
        _select = new CheckBox(context); _select.SetMinimumHeight(Dp(48));
        _select.SetTextSize(ComplexUnitType.Sp, 14);
        _select.CheckedChange += (_, args) =>
        {
            if (!_binding && _model is { } model)
            {
                var selected = args.IsChecked;
                Dispatcher.UIThread.Post(() => { if (ReferenceEquals(model, _model)) model.AllVisibleSelected = selected; });
            }
        };
        AddView(_select, Row(4));
        _bulk = new Spinner(context); _bulk.SetMinimumHeight(Dp(48));
        _bulk.ItemSelected += (_, args) =>
        {
            if (!_binding && _model is { } model && args.Position > 0 && args.Position <= model.Actions.Count && model.HasSelection)
            {
                var action = model.Actions[args.Position - 1];
                Dispatcher.UIThread.Post(() => { if (ReferenceEquals(model, _model)) model.BulkAction = action; });
                _bulk.SetSelection(0);
            }
        };
        AddView(_bulk, Row(8));
        _empty = Label(12); AddView(_empty, Row(0));
    }

    public void Bind(ImportReviewViewModel? model)
    {
        _model = model;
        if (model is not null) Refresh();
        else { _binding = true; _search.Text = string.Empty; _binding = false; }
    }

    public void Refresh()
    {
        if (_model is not { } model) return;
        _binding = true;
        try
        {
            var palette = NativeAccountPalette.Current;
            Colors(this, palette);
            Text(_breadcrumb, model.Breadcrumb); Text(_title, model.Title);
            Text(_steps, string.Join("  ›  ", model.StepSourceText, model.StepExportText, model.StepReviewText, model.StepImportText));
            _breadcrumb.SetTextColor(palette.Secondary); _steps.SetTextColor(palette.Accent);
            _summaryCard.Background = Surface(SemanticColor("BrushSurface", palette.Window), palette.Border);
            Text(_summary, model.SummaryTitle); Text(_file, model.SourceAndFile); _file.SetTextColor(palette.Secondary);
            var counts = new[] { model.TotalCount, model.ReadyCount, model.ConflictCount, model.DuplicateCount, model.UnsupportedCount, model.InvalidCount };
            var captions = new[] { model.TotalText, model.ReadyText, model.ConflictsText, model.DuplicatesText, model.UnsupportedText, model.InvalidText };
            for (var index = 0; index < 6; index++)
            {
                Text(_counts[index], counts[index].ToString(System.Globalization.CultureInfo.CurrentCulture));
                Text(_captions[index], captions[index]);
                _counts[index].SetTextColor(index == 5 ? palette.Danger : index == 2 ? SemanticColor("BrushWarning", palette.ImportHighlight)
                    : index == 1 ? SemanticColor("BrushSuccess", palette.Accent) : index == 3 ? palette.Accent : palette.Primary);
            }
            Text(_local, model.LocalText); _local.SetTextColor(palette.Secondary);
            Text(_excluded, model.ExcludedText); _excluded.SetTextColor(palette.Danger);
            _excluded.Visibility = model.HasExcluded ? ViewStates.Visible : ViewStates.Gone;
            _search.Hint = model.SearchText; _search.ContentDescription = model.SearchText;
            _search.SetHintTextColor(palette.Secondary);
            _search.Background = Surface(palette.Window, palette.Border);
            if (!_search.HasFocus) Text(_search, model.Search);
            UpdateOptions(_filter, ref _filterLabels, model.Categories.Select(category => category.Label).ToArray());
            _filter.ContentDescription = model.FilterText;
            _filter.SetSelection(model.Categories.ToList().IndexOf(model.SelectedCategory));
            Text(_select, model.SelectionText); _select.Checked = model.AllVisibleSelected;
            _select.ButtonTintList = global::Android.Content.Res.ColorStateList.ValueOf(palette.Accent);
            UpdateOptions(_bulk, ref _bulkLabels, new[] { model.ApplyText }.Concat(model.Actions.Select(action => action.Name)).ToArray());
            _bulk.ContentDescription = model.ApplyText; _bulk.Enabled = model.HasSelection;
            Text(_empty, model.NoResultsText); _empty.SetTextColor(palette.Secondary);
            _empty.Visibility = model.HasNoResults ? ViewStates.Visible : ViewStates.Gone;
        }
        finally { _binding = false; }
    }

    private void UpdateOptions(Spinner spinner, ref string[] previous, string[] values)
    {
        if (previous.SequenceEqual(values)) return;
        previous = values;
        spinner.Adapter = new ReviewOptions(Context!, values);
    }

    private sealed class ReviewOptions(Context context, string[] values) : BaseAdapter<string>
    {
        public override int Count => values.Length;
        public override string this[int position] => values[position];
        public override long GetItemId(int position) => position;
        public override View GetView(int position, View? convertView, ViewGroup? parent) => Make(position, convertView);
        public override View GetDropDownView(int position, View? convertView, ViewGroup? parent) => Make(position, convertView);
        private TextView Make(int position, View? convertView)
        {
            var text = convertView as TextView ?? new TextView(context);
            text.Text = values[position];
            text.SetTextSize(ComplexUnitType.Sp, 14);
            text.SetTextColor(NativeAccountPalette.Current.Primary);
            var density = context.Resources?.DisplayMetrics?.Density ?? 1;
            text.SetPadding((int)(12 * density), (int)(12 * density), (int)(12 * density), (int)(12 * density));
            text.SetBackgroundColor(NativeAccountPalette.Current.Window);
            return text;
        }
    }
}
