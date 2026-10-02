using System.Collections.Specialized;
using System.ComponentModel;
using Android.Content;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class NativeAccountRecyclerView : RecyclerView
{
    private readonly MobileShellViewModel _viewModel;
    private readonly NativeAccountAdapter _adapter;
    private readonly LinearLayoutManager _layoutManager;
    private readonly AccountScrollListener _scrollListener;
    private int _submitRevision;
    private int _revealRevision;
    private bool _disposed;

    public NativeAccountRecyclerView(Context context, MobileShellViewModel viewModel)
        : base(context)
    {
        _viewModel = viewModel;
        _layoutManager = new LinearLayoutManager(context, Vertical, false);
        _adapter = new NativeAccountAdapter(viewModel);
        _scrollListener = new AccountScrollListener(this);

        SetLayoutManager(_layoutManager);
        SetAdapter(_adapter);
        SetItemAnimator(null);
        HasFixedSize = true;
        SetItemViewCacheSize(10);
        GetRecycledViewPool().SetMaxRecycledViews(NativeAccountAdapter.AccountViewType, 10);
        AddOnScrollListener(_scrollListener);
        OverScrollMode = OverScrollMode.IfContentScrolls;
        VerticalScrollBarEnabled = false;

        _viewModel.Accounts.CollectionChanged += AccountsChanged;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _adapter.ReplaceAll(CaptureRows());
        Post(ReportVisibleAccounts);
        HandleRevealRequest(_viewModel.AccountRevealRequest);
    }

    public void DisposeHost()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _submitRevision);
        _viewModel.Accounts.CollectionChanged -= AccountsChanged;
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        RemoveOnScrollListener(_scrollListener);
        _viewModel.SetAccountListScrolling(false);
        _viewModel.SetRealizedAccounts([]);
        _adapter.DisposeAdapter();
        SetAdapter(null);
        Dispose();
    }

    internal void HandleScrollState(int newState)
    {
        var scrolling = newState != ScrollStateIdle;
        _viewModel.SetAccountListScrolling(scrolling);
        if (!scrolling) Post(ReportVisibleAccounts);
    }

    internal void ReportVisibleAccounts()
    {
        if (_disposed || _adapter.ItemCount == 0)
        {
            _viewModel.SetRealizedAccounts([]);
            return;
        }

        var first = _layoutManager.FindFirstVisibleItemPosition();
        var last = _layoutManager.FindLastVisibleItemPosition();
        if (first == NoPosition || last == NoPosition)
        {
            _viewModel.SetRealizedAccounts([]);
            return;
        }

        var accounts = _adapter.Items
            .Skip(Math.Max(0, first - 2))
            .Take(Math.Min(_adapter.ItemCount - Math.Max(0, first - 2), last - first + 5))
            .Select(row => row.Account)
            .ToArray();
        _viewModel.SetRealizedAccounts(accounts);
    }

    private void AccountsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        SubmitRows();

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MobileShellViewModel.EditAccountText)
            or nameof(MobileShellViewModel.DeleteAccountText)
            or nameof(MobileShellViewModel.CopyCodeText)
            or nameof(MobileShellViewModel.ShowQrText))
        {
            Post(_adapter.NotifyVisibleContentChanged);
        }
        else if (args.PropertyName == nameof(MobileShellViewModel.AccountRevealRequest))
        {
            HandleRevealRequest(_viewModel.AccountRevealRequest);
        }
    }

    private void SubmitRows()
    {
        if (_disposed) return;
        var next = CaptureRows();
        var previous = _adapter.Items;
        var revision = Interlocked.Increment(ref _submitRevision);

        _ = Task.Run(() => DiffUtil.CalculateDiff(new AccountDiffCallback(previous, next), true))
            .ContinueWith(
                completed =>
                {
                    if (completed.Status != TaskStatus.RanToCompletion || _disposed)
                    {
                        completed.Exception?.Handle(_ => true);
                        return;
                    }

                    Post(() =>
                    {
                        if (_disposed || revision != Volatile.Read(ref _submitRevision)) return;
                        _adapter.ApplyDiff(next, completed.Result);
                        ReportVisibleAccounts();
                        HandleRevealRequest(_viewModel.AccountRevealRequest);
                    });
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private NativeAccountRow[] CaptureRows() =>
        _viewModel.Accounts.Select(NativeAccountRow.FromAccount).ToArray();

    private void HandleRevealRequest(MobileAccountRevealRequest? request)
    {
        if (_disposed || request is null || request.Revision <= _revealRevision) return;
        if (!AdapterMatchesCurrentAccounts()) return;

        var position = _adapter.FindPosition(request.AccountId);
        if (position == NoPosition) return;

        _revealRevision = request.Revision;
        var offset = request.AlignToTop
            ? 0
            : Math.Max(
                0,
                (Height - NativeAccountRowView.RowHeight(Context!)) / 2);
        _layoutManager.ScrollToPositionWithOffset(position, offset);
        if (request.Highlight)
            PostDelayed(() => PulseRevealedAccount(position, request.Revision, 0), 50);
        else
            Post(ReportVisibleAccounts);
    }

    private bool AdapterMatchesCurrentAccounts()
    {
        if (_adapter.ItemCount != _viewModel.Accounts.Count) return false;

        for (var index = 0; index < _adapter.ItemCount; index++)
        {
            if (_adapter.Items[index].Id != _viewModel.Accounts[index].Id)
                return false;
        }

        return true;
    }

    private void PulseRevealedAccount(int position, int revision, int attempt)
    {
        if (_disposed || revision != _revealRevision) return;
        if (FindViewHolderForAdapterPosition(position) is NativeAccountAdapter.AccountViewHolder holder)
        {
            holder.Row.PulseHighlight();
            ReportVisibleAccounts();
            return;
        }

        if (attempt < 5)
            PostDelayed(() => PulseRevealedAccount(position, revision, attempt + 1), 50);
    }

    private sealed class AccountScrollListener(NativeAccountRecyclerView owner)
        : RecyclerView.OnScrollListener
    {
        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState) =>
            owner.HandleScrollState(newState);

        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy)
        {
            if (recyclerView.ScrollState == ScrollStateIdle)
                owner.ReportVisibleAccounts();
        }
    }
}

internal sealed class NativeAccountAdapter : RecyclerView.Adapter
{
    internal const int AccountViewType = 1;
    private readonly MobileShellViewModel _viewModel;
    private NativeAccountRow[] _items = [];
    private NativeAccountRowView? _openRow;

    public NativeAccountAdapter(MobileShellViewModel viewModel)
    {
        _viewModel = viewModel;
        HasStableIds = true;
    }

    public NativeAccountRow[] Items => _items;
    public override int ItemCount => _items.Length;

    public override long GetItemId(int position)
    {
        Span<byte> bytes = stackalloc byte[16];
        _items[position].Id.TryWriteBytes(bytes);
        return BitConverter.ToInt64(bytes[..8]) ^ BitConverter.ToInt64(bytes[8..]);
    }

    public override int GetItemViewType(int position) => AccountViewType;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var row = new NativeAccountRowView(parent.Context!, _viewModel, OpenRow);
        row.LayoutParameters = new RecyclerView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            NativeAccountRowView.RowHeight(parent.Context!));
        return new AccountViewHolder(row);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) =>
        ((AccountViewHolder)holder).Bind(_items[position]);

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is AccountViewHolder accountHolder) accountHolder.Unbind();
        base.OnViewRecycled(holder);
    }

    public override void OnViewDetachedFromWindow(Java.Lang.Object holder)
    {
        if (holder is AccountViewHolder accountHolder) accountHolder.Row.StopAnimating();
        base.OnViewDetachedFromWindow(holder);
    }

    public override void OnViewAttachedToWindow(Java.Lang.Object holder)
    {
        base.OnViewAttachedToWindow(holder);
        if (holder is AccountViewHolder accountHolder) accountHolder.Row.StartAnimating();
    }

    internal void ReplaceAll(NativeAccountRow[] items)
    {
        _items = items;
        NotifyDataSetChanged();
    }

    internal void ApplyDiff(NativeAccountRow[] items, DiffUtil.DiffResult diff)
    {
        CloseOpenRow();
        _items = items;
        diff.DispatchUpdatesTo(this);
    }

    internal void NotifyVisibleContentChanged() =>
        NotifyItemRangeChanged(0, ItemCount, NativeAccountRowView.LocalizationPayload);

    internal int FindPosition(Guid accountId)
    {
        for (var index = 0; index < _items.Length; index++)
        {
            if (_items[index].Id == accountId) return index;
        }

        return RecyclerView.NoPosition;
    }

    internal void DisposeAdapter()
    {
        CloseOpenRow();
        _items = [];
        NotifyDataSetChanged();
    }

    private void OpenRow(NativeAccountRowView row)
    {
        if (row.IsActionsOpen)
        {
            if (!ReferenceEquals(_openRow, row)) _openRow?.CloseActions();
            _openRow = row;
        }
        else if (ReferenceEquals(_openRow, row))
        {
            _openRow = null;
        }
    }

    private void CloseOpenRow()
    {
        _openRow?.CloseActions();
        _openRow = null;
    }

    internal sealed class AccountViewHolder(NativeAccountRowView row) : RecyclerView.ViewHolder(row)
    {
        public NativeAccountRowView Row { get; } = row;
        public void Bind(NativeAccountRow item) => Row.Bind(item);
        public void Unbind() => Row.Unbind();
    }
}

internal sealed class AccountDiffCallback(
    NativeAccountRow[] oldItems,
    NativeAccountRow[] newItems) : DiffUtil.Callback
{
    public override int OldListSize => oldItems.Length;
    public override int NewListSize => newItems.Length;

    public override bool AreItemsTheSame(int oldItemPosition, int newItemPosition) =>
        oldItems[oldItemPosition].Id == newItems[newItemPosition].Id;

    public override bool AreContentsTheSame(int oldItemPosition, int newItemPosition) =>
        oldItems[oldItemPosition].HasSameVisuals(newItems[newItemPosition]);
}

internal sealed record NativeAccountRow(
    Guid Id,
    MobileAccountItem Account,
    string Issuer,
    string AccountName,
    string CustomPeriodLabel,
    string BrandInitials,
    string BrandBackgroundColor,
    string? BrandIconData,
    bool ShowIssuerLogo,
    bool IsFavorite)
{
    public static NativeAccountRow FromAccount(MobileAccountItem account) => new(
        account.Id,
        account,
        account.Issuer,
        account.AccountName,
        account.CustomPeriodLabel,
        account.Brand.Initials,
        account.Brand.BackgroundColor,
        account.Brand.IconData,
        account.ShowIssuerLogo,
        account.IsFavorite);

    public bool HasSameVisuals(NativeAccountRow other) =>
        ReferenceEquals(Account, other.Account)
        && Issuer == other.Issuer
        && AccountName == other.AccountName
        && CustomPeriodLabel == other.CustomPeriodLabel
        && BrandInitials == other.BrandInitials
        && BrandBackgroundColor == other.BrandBackgroundColor
        && BrandIconData == other.BrandIconData
        && ShowIssuerLogo == other.ShowIssuerLogo
        && IsFavorite == other.IsFavorite;
}
