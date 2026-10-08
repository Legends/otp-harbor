using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class SearchableComboBox : TemplatedControl
{
    private Button? _clearButton;
    private Button? _dropDownButton;
    private ListBox? _itemsList;
    private Popup? _popup;
    private TextBox? _searchBox;
    private double _effectivePopupWidth = 320d;
    private IReadOnlyList<object> _filteredItems = [];

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableComboBox, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchableComboBox, object?>(
            nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SearchableComboBox, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<string> SearchPlaceholderProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(
            nameof(SearchPlaceholder),
            string.Empty);

    public static readonly StyledProperty<string> ClearSearchTextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(
            nameof(ClearSearchText),
            string.Empty);

    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(
            nameof(SearchText),
            string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> PopupWidthProperty =
        AvaloniaProperty.Register<SearchableComboBox, double>(nameof(PopupWidth), 320d);

    public static readonly StyledProperty<double> PopupMaxHeightProperty =
        AvaloniaProperty.Register<SearchableComboBox, double>(nameof(PopupMaxHeight), 300d);

    public static readonly StyledProperty<double> PreferredWidthProperty =
        AvaloniaProperty.Register<SearchableComboBox, double>(nameof(PreferredWidth), double.NaN);

    public static readonly DirectProperty<SearchableComboBox, IReadOnlyList<object>> FilteredItemsProperty =
        AvaloniaProperty.RegisterDirect<SearchableComboBox, IReadOnlyList<object>>(
            nameof(FilteredItems),
            static control => control.FilteredItems);

    public static readonly DirectProperty<SearchableComboBox, double> EffectivePopupWidthProperty =
        AvaloniaProperty.RegisterDirect<SearchableComboBox, double>(
            nameof(EffectivePopupWidth),
            static control => control.EffectivePopupWidth);

    static SearchableComboBox()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SearchableComboBox>(
            static (control, _) => control.RefreshFilteredItems());
        SearchTextProperty.Changed.AddClassHandler<SearchableComboBox>(
            static (control, _) =>
            {
                control.RefreshFilteredItems();
                control.UpdateClearButton();
            });
        SelectedItemProperty.Changed.AddClassHandler<SearchableComboBox>(
            static (control, _) => control.SyncSelectedItem());
        PopupWidthProperty.Changed.AddClassHandler<SearchableComboBox>(
            static (control, _) => control.UpdateEffectivePopupWidth());
        AffectsMeasure<SearchableComboBox>(PreferredWidthProperty);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public string SearchPlaceholder
    {
        get => GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value ?? string.Empty);
    }

    public string ClearSearchText
    {
        get => GetValue(ClearSearchTextProperty);
        set => SetValue(ClearSearchTextProperty, value ?? string.Empty);
    }

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value ?? string.Empty);
    }

    public double PopupWidth
    {
        get => GetValue(PopupWidthProperty);
        set => SetValue(PopupWidthProperty, value);
    }

    public double PopupMaxHeight
    {
        get => GetValue(PopupMaxHeightProperty);
        set => SetValue(PopupMaxHeightProperty, value);
    }

    public double PreferredWidth
    {
        get => GetValue(PreferredWidthProperty);
        set => SetValue(PreferredWidthProperty, value);
    }

    public IReadOnlyList<object> FilteredItems
    {
        get => _filteredItems;
        private set => SetAndRaise(FilteredItemsProperty, ref _filteredItems, value);
    }

    public double EffectivePopupWidth
    {
        get => _effectivePopupWidth;
        private set => SetAndRaise(
            EffectivePopupWidthProperty,
            ref _effectivePopupWidth,
            value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        DetachHandlers();
        base.OnApplyTemplate(e);

        _clearButton = e.NameScope.Find<Button>("PART_ClearSearch");
        _dropDownButton = e.NameScope.Find<Button>("PART_DropDownButton");
        _itemsList = e.NameScope.Find<ListBox>("PART_ItemsList");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _searchBox = e.NameScope.Find<TextBox>("PART_SearchBox");

        AttachHandlers();
        RefreshFilteredItems();
        SyncSelectedItem();
        UpdateClearButton();
        UpdateEffectivePopupWidth();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        if (!double.IsFinite(PreferredWidth) || PreferredWidth <= 0)
            return measured;

        var width = double.IsPositiveInfinity(availableSize.Width)
            ? PreferredWidth
            : Math.Min(PreferredWidth, availableSize.Width);
        return new Size(width, measured.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseDropDown();
        DetachHandlers();
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachHandlers()
    {
        if (_clearButton is not null) _clearButton.Click += ClearSearch;
        if (_dropDownButton is not null) _dropDownButton.Click += ToggleDropDown;
        if (_itemsList is not null) _itemsList.SelectionChanged += SelectItem;
        if (_popup is not null)
        {
            _popup.Opened += PopupOpened;
            _popup.Closed += PopupClosed;
        }
        if (_searchBox is not null)
        {
            _searchBox.KeyDown += SearchKeyDown;
        }
    }

    private void DetachHandlers()
    {
        if (_clearButton is not null) _clearButton.Click -= ClearSearch;
        if (_dropDownButton is not null) _dropDownButton.Click -= ToggleDropDown;
        if (_itemsList is not null) _itemsList.SelectionChanged -= SelectItem;
        if (_popup is not null)
        {
            _popup.Opened -= PopupOpened;
            _popup.Closed -= PopupClosed;
        }
        if (_searchBox is not null)
        {
            _searchBox.KeyDown -= SearchKeyDown;
        }

        _clearButton = null;
        _dropDownButton = null;
        _itemsList = null;
        _popup = null;
        _searchBox = null;
    }

    private void ToggleDropDown(object? sender, RoutedEventArgs e)
    {
        if (_popup is null) return;
        _popup.IsOpen = !_popup.IsOpen;
    }

    private void PopupOpened(object? sender, EventArgs e)
    {
        UpdateEffectivePopupWidth();
        SyncSelectedItem();
        Dispatcher.UIThread.Post(
            () => _searchBox?.Focus(),
            DispatcherPriority.Input);
    }

    private void PopupClosed(object? sender, EventArgs e) => ResetSearch();

    private void CloseDropDown()
    {
        if (_popup is not null) _popup.IsOpen = false;
    }

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseDropDown();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter || FilteredItems.Count == 0) return;

        SelectedItem = _itemsList?.SelectedItem ?? FilteredItems[0];
        CloseDropDown();
        e.Handled = true;
    }

    private void ClearSearch(object? sender, RoutedEventArgs e)
    {
        if (_searchBox is null) return;
        SearchText = string.Empty;
        _searchBox.Focus();
    }

    private void SelectItem(object? sender, SelectionChangedEventArgs e)
    {
        if (_popup?.IsOpen != true || _itemsList?.SelectedItem is null) return;

        SelectedItem = _itemsList.SelectedItem;
        CloseDropDown();
    }

    private void ResetSearch()
    {
        if (!string.IsNullOrEmpty(SearchText))
            SearchText = string.Empty;
        else
            RefreshFilteredItems();
    }

    private void RefreshFilteredItems()
    {
        var searchText = SearchText.Trim();
        var items = ItemsSource?.Cast<object>() ?? [];
        FilteredItems = string.IsNullOrEmpty(searchText)
            ? items.ToArray()
            : items.Where(item =>
                    item.ToString()?.Contains(
                        searchText,
                        StringComparison.CurrentCultureIgnoreCase) == true)
                .ToArray();
        SyncSelectedItem();
    }

    private void SyncSelectedItem()
    {
        if (_itemsList is not null && !Equals(_itemsList.SelectedItem, SelectedItem))
            _itemsList.SelectedItem = SelectedItem;
    }

    private void UpdateClearButton()
    {
        if (_clearButton is not null)
            _clearButton.IsVisible = !string.IsNullOrEmpty(SearchText);
    }

    private void UpdateEffectivePopupWidth()
    {
        var desiredWidth = Math.Max(Bounds.Width, PopupWidth);
        var topLevel = TopLevel.GetTopLevel(this);
        var origin = topLevel is null
            ? null
            : this.TranslatePoint(new Point(0, 0), topLevel);
        if (topLevel is not null && origin.HasValue)
        {
            const double outerMargin = 12d;
            var availableWidth = Math.Max(
                Bounds.Width,
                topLevel.ClientSize.Width - origin.Value.X - outerMargin);
            desiredWidth = Math.Min(desiredWidth, availableWidth);
        }

        EffectivePopupWidth = desiredWidth;
    }
}
