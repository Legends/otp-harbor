using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public enum ImportReviewCategory { All, Ready, Conflicts, Duplicates, Unsupported, Invalid }

public sealed class ImportReviewViewModel : INotifyPropertyChanged
{
    private readonly Func<string, string> _localize;
    private readonly AccountImportReadSummary? _summary;
    private string _search = string.Empty;
    private CategoryOption _category;
    private ActionOption? _bulkAction;
    private bool _completed;
    private IReadOnlyList<ReviewRow> _visibleRows = [];

    public ImportReviewViewModel(AccountImportPreview preview, AccountImportReadSummary? summary,
        string sourceName, string fileName, Func<string, string> localize)
    {
        _localize = localize;
        _summary = summary;
        SourceName = Safe(sourceName);
        FileName = Safe(fileName);
        Actions = Enum.GetValues<AccountImportConflictAction>().Select(action => new ActionOption(this, action)).ToArray();
        Rows = preview.Entries.Select((entry, index) => new ReviewRow(this, entry,
            entry.ImportIndex < 0 ? index : entry.ImportIndex,
            preview.ChangedConflicts.FirstOrDefault(conflict => conflict.ImportIndex == (entry.ImportIndex < 0 ? index : entry.ImportIndex)))).ToArray();
        Categories = Enum.GetValues<ImportReviewCategory>().Select(category => new CategoryOption(this, category)).ToArray();
        _category = Categories[0];
        _visibleRows = Rows.OrderByDescending(row => row.IsConflict).ThenByDescending(row => row.Category == ImportReviewCategory.Duplicates).ToArray();
        MobileItems.Add(this);
        foreach (var row in _visibleRows) MobileItems.Add(row);
        ImportCommand = new ReviewCommand(_ => Complete(true), () => CanImport);
        BackCommand = new ReviewCommand(_ => Complete(false, goBack: true));
        CancelCommand = new ReviewCommand(_ => Cancel());
        FilterCommand = new ReviewCommand(value => { if (value is CategoryOption category) SelectedCategory = category; });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<AccountImportResolution?>? CloseRequested;
    public IReadOnlyList<ReviewRow> Rows { get; }
    public ObservableCollection<object> MobileItems { get; } = [];
    public IReadOnlyList<CategoryOption> Categories { get; }
    public IReadOnlyList<ActionOption> Actions { get; }
    public ICommand ImportCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }
    public bool BackRequested { get; private set; }
    public ICommand FilterCommand { get; }
    public string SourceName { get; }
    public string FileName { get; }
    public string SourceAndFile => Format(ReviewStringKeys.Identity, SourceName, FileName);
    public string StepsText => Text(ReviewStringKeys.Steps);
    public string StepSourceText => Text(ReviewStringKeys.StepSource);
    public string StepExportText => Text(ReviewStringKeys.StepExport);
    public string StepReviewText => Text(ReviewStringKeys.StepReview);
    public string StepImportText => Text(ReviewStringKeys.StepImport);
    public int TotalCount => Math.Max(Rows.Count, _summary?.SupportedCount ?? 0) + UnsupportedCount + InvalidCount;
    public int ReadyCount => Rows.Count(row => row.Category == ImportReviewCategory.Ready);
    public int ConflictCount => Rows.Count(row => row.IsConflict);
    public int DuplicateCount => Rows.Count(row => row.Category == ImportReviewCategory.Duplicates);
    public int UnsupportedCount => (_summary?.UnsupportedCount ?? 0) + (_summary?.NonOtpCount ?? 0);
    public int InvalidCount => _summary?.InvalidCount ?? 0;
    public int UnresolvedCount => Rows.Count(row => row.IsConflict && row.SelectedAction is null);
    public int ImportCount => ReadyCount + Rows.Count(row => row.IsConflict && row.SelectedAction?.Action is AccountImportConflictAction.Replace or AccountImportConflictAction.KeepBoth);
    public bool CanImport => !_completed && InvalidCount == 0 && (_summary?.UnsupportedCount ?? 0) == 0 && UnresolvedCount == 0 && ImportCount > 0;
    public string ImportText => Format(ReviewStringKeys.ImportCount, ImportCount);
    public string ReviewStatus => UnresolvedCount > 0 ? Format(ReviewStringKeys.Unresolved, UnresolvedCount)
        : CanImport ? ReadyText : HasExcluded ? ExcludedText : Rows.Count == 0 ? NoResultsText : Text(ReviewStringKeys.DuplicateDescription);
    public bool HasExcluded => UnsupportedCount + InvalidCount > 0;
    public string ExcludedText => InvalidCount + (_summary?.UnsupportedCount ?? 0) == 0
        ? Format(ReviewStringKeys.ExcludedNonOtp, _summary?.NonOtpCount ?? 0)
        : Format(ReviewStringKeys.Excluded, _summary?.NonOtpCount ?? 0, _summary?.UnsupportedCount ?? 0, InvalidCount);
    public string SelectionText => Format(ReviewStringKeys.SelectedCount, Rows.Count(row => row.IsSelected));
    public bool HasSelection => Rows.Any(row => row.IsSelected && row.IsConflict);
    public string Breadcrumb => Text(ReviewStringKeys.Breadcrumb);
    public string Title => Text(ReviewStringKeys.Title);
    public string Description => Text(ReviewStringKeys.Description);
    public string SummaryTitle => Text(ReviewStringKeys.Summary);
    public string TotalText => Text(ReviewStringKeys.Total);
    public string ReadyText => Text(ReviewStringKeys.Ready);
    public string ConflictsText => Text(ReviewStringKeys.Conflicts);
    public string DuplicatesText => Text(ReviewStringKeys.Duplicates);
    public string UnsupportedText => Text(ReviewStringKeys.Unsupported);
    public string InvalidText => Text(ReviewStringKeys.Invalid);
    public string LocalText => Text(MigrationStringKeys.LocalProcessing);
    public string SearchText => Text(ReviewStringKeys.Search);
    public string FilterText => Text(ReviewStringKeys.Filter);
    public string ApplyText => Text(ReviewStringKeys.Apply);
    public string BackText => Text(ReviewStringKeys.Back);
    public string CancelText => Text(ReviewStringKeys.Cancel);
    public string AccountText => Text(ReviewStringKeys.Account);
    public string IssueText => Text(ReviewStringKeys.Issue);
    public string ExistingText => Text(ReviewStringKeys.Existing);
    public string IncomingText => Text(ReviewStringKeys.Incoming);
    public string ActionText => Text(ReviewStringKeys.Action);
    public string NoResultsText => Text(ReviewStringKeys.NoResults);
    public bool ShowExcludedDetails => HasExcluded && VisibleRows.Count == 0
        && SelectedCategory.Category is ImportReviewCategory.Unsupported or ImportReviewCategory.Invalid or ImportReviewCategory.All;
    public bool HasNoResults => VisibleRows.Count == 0 && !ShowExcludedDetails;
    public IReadOnlyList<ReviewRow> VisibleRows => _visibleRows;
    private void Filter()
    {
        _visibleRows = Rows.Where(row =>
        (SelectedCategory.Category == ImportReviewCategory.All || row.Category == SelectedCategory.Category)
        && (string.IsNullOrWhiteSpace(Search) || (row.Identity + " " + row.IssueText + " " + SourceName)
            .Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(row => row.IsConflict).ThenByDescending(row => row.Category == ImportReviewCategory.Duplicates).ToArray();
        foreach (var category in Categories) category.Refresh();
        while (MobileItems.Count > 1) MobileItems.RemoveAt(MobileItems.Count - 1);
        foreach (var row in _visibleRows) MobileItems.Add(row);
        Notify();
    }
    public string Search { get => _search; set { _search = value; Filter(); } }
    public CategoryOption SelectedCategory { get => _category; set { if (value is null) return; _category = value; Filter(); } }
    public ActionOption? BulkAction
    {
        get => _bulkAction;
        set
        {
            _bulkAction = value;
            if (value is not null) foreach (var row in Rows.Where(row => row.IsSelected && row.IsConflict)) row.SelectedAction = value;
            _bulkAction = null;
            Notify();
        }
    }
    public bool AllVisibleSelected
    {
        get => VisibleRows.Any(row => row.IsConflict) && VisibleRows.Where(row => row.IsConflict).All(row => row.IsSelected);
        set { foreach (var row in VisibleRows.Where(row => row.IsConflict)) row.IsSelected = value; Notify(); }
    }

    public void Cancel() => Complete(false);
    public void RefreshLanguage()
    {
        foreach (var row in Rows) row.Refresh();
        foreach (var category in Categories) category.Refresh();
        foreach (var action in Actions) action.Refresh();
        Notify();
    }
    public AccountImportResolution? CreateResolution() => CanImport ? new(Rows.Where(row => row.IsConflict)
        .Select(row => new AccountImportConflictResolution(row.ImportIndex, row.SelectedAction!.Action)).ToArray()) : null;
    private void Complete(bool import, bool goBack = false)
    {
        if (_completed || import && !CanImport) return;
        var resolution = import ? CreateResolution() : null;
        BackRequested = goBack;
        _completed = true;
        Notify();
        CloseRequested?.Invoke(resolution);
    }
    internal string Text(string key) => _localize(key);
    internal string Format(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, Text(key), values);
    internal void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        ((ReviewCommand)ImportCommand).Refresh();
    }
    internal static string Safe(string? value) => string.Concat((value ?? string.Empty).Select(character => char.IsControl(character) ? ' ' : character));

    public sealed class CategoryOption(ImportReviewViewModel owner, ImportReviewCategory category) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        public bool IsSelected => ReferenceEquals(owner.SelectedCategory, this);
        public ImportReviewCategory Category { get; } = category;
        public string Name => owner.Text(ReviewStringKeys.Category(Category));
        public int Count => Category switch
        {
            ImportReviewCategory.All => owner.TotalCount,
            ImportReviewCategory.Ready => owner.ReadyCount,
            ImportReviewCategory.Conflicts => owner.ConflictCount,
            ImportReviewCategory.Duplicates => owner.DuplicateCount,
            ImportReviewCategory.Unsupported => owner.UnsupportedCount,
            _ => owner.InvalidCount
        };
        public string Label => owner.Format(ReviewStringKeys.CategoryCount, Name, Count);
        public ICommand Command => owner.FilterCommand;
        public override string ToString() => Label;
    }
    public sealed class ActionOption(ImportReviewViewModel owner, AccountImportConflictAction action) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        public AccountImportConflictAction Action { get; } = action;
        public string Name => owner.Text(ReviewStringKeys.ActionKey(Action));
        public override string ToString() => Name;
    }

    public sealed class ReviewRow : INotifyPropertyChanged
    {
        private readonly ImportReviewViewModel _owner;
        private readonly AccountImportEntryPreview _entry;
        private readonly AccountImportConflict? _conflict;
        private ActionOption? _action;
        private bool _selected;
        private bool _expanded;
        public ReviewRow(ImportReviewViewModel owner, AccountImportEntryPreview entry, int importIndex, AccountImportConflict? conflict)
        { _owner = owner; _entry = entry; ImportIndex = importIndex; _conflict = conflict; _expanded = conflict is not null; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public int ImportIndex { get; }
        public string GroupName => "import-review-" + ImportIndex.ToString(CultureInfo.InvariantCulture);
        public bool IsConflict => _conflict is not null;
        public bool IsExpanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
        public ImportReviewCategory Category => IsConflict ? ImportReviewCategory.Conflicts : _entry.Existing is not null ? ImportReviewCategory.Duplicates : ImportReviewCategory.Ready;
        public string Issuer => Safe(_entry.Issuer);
        public string AccountName => Safe(_entry.AccountName);
        public string Identity => _owner.Format(ReviewStringKeys.Identity, Issuer, AccountName);
        public string Parameters => _owner.Format(ReviewStringKeys.Parameters, _entry.Algorithm, _entry.Digits, _entry.PeriodSeconds);
        public string ExistingParameters => _entry.Existing is { } existing
            ? (_entry.ExistingIsFromExport ? _owner.Text(ReviewStringKeys.EarlierExport) + Environment.NewLine : string.Empty)
                + _owner.Format(ReviewStringKeys.ExistingParameters, Safe(existing.Issuer), Safe(existing.AccountName), existing.Algorithm, existing.Digits, existing.PeriodSeconds)
            : _owner.Text(ReviewStringKeys.NoExisting);
        public string IncomingParameters => _owner.Format(ReviewStringKeys.ExistingParameters, Issuer, AccountName, _entry.Algorithm, _entry.Digits, _entry.PeriodSeconds);
        public string IssueText => _owner.Text(!IsConflict ? Category == ImportReviewCategory.Duplicates ? ReviewStringKeys.ExactDuplicate : ReviewStringKeys.NewAccount
            : _conflict!.SecretChanged ? ReviewStringKeys.SecretDiffers
            : _conflict.IssuerChanged || _conflict.AccountNameChanged ? ReviewStringKeys.LabelDiffers : ReviewStringKeys.ParametersDiffer);
        public string IssueDescription => _owner.Text(_entry.ExistingIsFromExport ? ReviewStringKeys.EarlierDescription : IsConflict ? _conflict!.SecretChanged ? ReviewStringKeys.SecretDescription : ReviewStringKeys.ChangeDescription
            : Category == ImportReviewCategory.Duplicates ? ReviewStringKeys.DuplicateDescription : ReviewStringKeys.ReadyDescription);
        public string ChangedFieldsText
        {
            get
            {
                if (_conflict is null) return string.Empty;
                var fields = new (bool Changed, string Key)[]
                {
                    (_conflict.IssuerChanged, ReviewStringKeys.FieldIssuer), (_conflict.AccountNameChanged, ReviewStringKeys.FieldAccount),
                    (_conflict.SecretChanged, ReviewStringKeys.FieldSecret), (_conflict.AlgorithmChanged, ReviewStringKeys.FieldAlgorithm),
                    (_conflict.DigitsChanged, ReviewStringKeys.FieldDigits), (_conflict.PeriodChanged, ReviewStringKeys.FieldPeriod),
                    (_conflict.FavoriteChanged, ReviewStringKeys.FieldFavorite), (_conflict.GroupMembershipChanged, ReviewStringKeys.FieldGroups)
                };
                return _owner.Format(ReviewStringKeys.Changes,
                    string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", fields.Where(change => change.Changed).Select(change => _owner.Text(change.Key))));
            }
        }
        public string ChoicesHelp => _owner.Text(ReviewStringKeys.ChoicesHelp);
        public string ActionSummary => IsConflict ? SelectedAction?.Name ?? _owner.Text(ReviewStringKeys.ChooseAction) : _owner.Text(Category == ImportReviewCategory.Duplicates ? ReviewStringKeys.Skip : ReviewStringKeys.Add);
        public string ExistingText => _entry.ExistingIsFromExport ? _owner.Text(ReviewStringKeys.EarlierExport) : _owner.ExistingText;
        public string IncomingText => _owner.IncomingText;
        public string ActionText => _owner.ActionText;
        public string ReplaceText => _owner.Text(ReviewStringKeys.Replace);
        public string KeepBothText => _owner.Text(ReviewStringKeys.KeepBoth);
        public string SkipText => _owner.Text(ReviewStringKeys.Skip);
        public IReadOnlyList<ActionOption> Actions => _owner.Actions;
        public ActionOption? SelectedAction { get => _action; set { if (_action == value) return; _action = value; Refresh(); _owner.Notify(); } }
        public bool IsSelected { get => _selected; set { _selected = value; Refresh(); _owner.Notify(); } }
        public bool IsReplace { get => SelectedAction?.Action == AccountImportConflictAction.Replace; set { if (value) SelectedAction = Actions.Single(option => option.Action == AccountImportConflictAction.Replace); } }
        public bool IsKeepBoth { get => SelectedAction?.Action == AccountImportConflictAction.KeepBoth; set { if (value) SelectedAction = Actions.Single(option => option.Action == AccountImportConflictAction.KeepBoth); } }
        public bool IsSkip { get => SelectedAction?.Action == AccountImportConflictAction.Skip; set { if (value) SelectedAction = Actions.Single(option => option.Action == AccountImportConflictAction.Skip); } }
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private sealed class ReviewCommand(Action<object?> execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
