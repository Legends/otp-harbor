using System.Collections.Specialized;
using System.ComponentModel;
using Android.Content;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Avalonia.Threading;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Shared.Branding;

namespace TOTP.Avalonia.Android;

internal sealed class NativeAccountRecyclerView : RecyclerView
{
    private const int FullIconPrewarmAccountLimit = 640;
    private const int FullIconPrewarmBrandLimit = 40;
    private const int InitialIconPrewarmAccountCount = 640;
    private const int IconPrewarmBatchSize = 100;
    private const int RuntimeIconPrewarmConcurrency = 16;

    private readonly MobileShellViewModel _viewModel;
    private readonly NativeBrandIconCache _brandIcons;
    private readonly NativeAccountAdapter _adapter;
    private readonly LinearLayoutManager _layoutManager;
    private readonly AccountScrollListener _scrollListener;
    private readonly Action _dismissAccountSearchFocus;
    private readonly NativeDeleteConfirmationDialog _deleteConfirmationDialog;
    private readonly object _pendingRuntimeIconRefreshLock = new();
    private readonly HashSet<string> _pendingRuntimeIconRefreshes = new(StringComparer.OrdinalIgnoreCase);
    private int _submitRevision;
    private int _revealRevision;
    private int _iconPrewarmRequestKey = int.MinValue;
    private bool _hasAccountListScrolled;
    private bool _hasResolvableBrands;
    private bool _prewarmAllIcons;
    private bool _runtimeIconRefreshPosted;
    private CancellationTokenSource? _initialIconPrewarmLifetime;
    private CancellationTokenSource? _iconPrefetchLifetime;
    private bool _disposed;

    public NativeAccountRecyclerView(
        Context context,
        MobileShellViewModel viewModel,
        NativeBrandIconCache brandIcons,
        Action dismissAccountSearchFocus)
        : base(context)
    {
        _viewModel = viewModel;
        _dismissAccountSearchFocus = dismissAccountSearchFocus;
        _layoutManager = new LinearLayoutManager(context, Vertical, false);
        _brandIcons = brandIcons;
        _brandIcons.RuntimeIconReady += RuntimeIconReady;
        _adapter = new NativeAccountAdapter(viewModel, _brandIcons);
        _scrollListener = new AccountScrollListener(this);

        SetLayoutManager(_layoutManager);
        SetItemAnimator(null);
        HasFixedSize = true;
        SetItemViewCacheSize(10);
        GetRecycledViewPool().SetMaxRecycledViews(NativeAccountAdapter.AccountViewType, 10);
        AddOnScrollListener(_scrollListener);
        OverScrollMode = OverScrollMode.IfContentScrolls;
        VerticalScrollBarEnabled = false;

        _viewModel.Accounts.CollectionChanged += AccountsChanged;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        _deleteConfirmationDialog = new NativeDeleteConfirmationDialog(this, viewModel);
        _adapter.ReplaceAll(CaptureRows());
        RefreshIconPrewarmProfile();
        SetAdapter(_adapter);
        _ = StartInitialIconPrewarm();
        Post(() =>
        {
            ReportVisibleAccounts();
            HandleRevealRequest(_viewModel.AccountRevealRequest);
            ScheduleIconPrewarm();
        });
    }

    public override bool DispatchTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent?.ActionMasked == MotionEventActions.Down)
            _dismissAccountSearchFocus();

        return base.DispatchTouchEvent(motionEvent);
    }

    public void DisposeHost()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAllIconPrewarm();
        Interlocked.Increment(ref _submitRevision);
        _viewModel.Accounts.CollectionChanged -= AccountsChanged;
        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _deleteConfirmationDialog.Dispose();
        _viewModel.SetAccountListScrolling(false);
        _viewModel.SetRealizedAccounts([]);
        _adapter.DisposeAdapter();
        _brandIcons.RuntimeIconReady -= RuntimeIconReady;
        Dispose();
    }

    internal void HandleScrollState(int newState)
    {
        var scrolling = newState != ScrollStateIdle;
        _adapter.SetListScrolling(scrolling);
        UpdateAttachedRowsScrolling(scrolling);
        _viewModel.SetAccountListScrolling(scrolling);
        if (scrolling)
        {
            _hasAccountListScrolled = true;
            ScheduleIconPrewarm();
        }
        else
        {
            Post(() =>
            {
                ReportVisibleAccounts();
                ScheduleIconPrewarm();
            });
        }
    }

    private void UpdateAttachedRowsScrolling(bool scrolling)
    {
        for (var index = 0; index < ChildCount; index++)
        {
            var child = GetChildAt(index);
            if (child is not null
                && GetChildViewHolder(child) is NativeAccountAdapter.AccountViewHolder holder)
            {
                holder.Row.SetListScrolling(scrolling);
            }
        }
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

    internal void HandleIdleScroll()
    {
        ReportVisibleAccounts();
        ScheduleIconPrewarm();
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
            Post(ScheduleIconPrewarm);
        }
        else if (args.PropertyName == nameof(MobileShellViewModel.AccountBrandCatalogRevision))
        {
            SubmitRows();
        }
    }

    private void SubmitRows()
    {
        if (_disposed) return;
        var revision = Interlocked.Increment(ref _submitRevision);
        Post(() =>
        {
            if (_disposed || revision != Volatile.Read(ref _submitRevision)) return;
            CancelAllIconPrewarm();
            _iconPrewarmRequestKey = int.MinValue;
            _hasAccountListScrolled = false;
            _adapter.ReplaceAll(CaptureRows());
            RefreshIconPrewarmProfile();
            _ = StartInitialIconPrewarm();
            ReportVisibleAccounts();
            HandleRevealRequest(_viewModel.AccountRevealRequest);
            ScheduleIconPrewarm();
        });
    }

    private NativeAccountRow[] CaptureRows() =>
        _viewModel.Accounts.Select(NativeAccountRow.FromAccount).ToArray();

    private void RefreshIconPrewarmProfile()
    {
        var items = _adapter.Items;
        var distinctBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Brand.Id)) distinctBrands.Add(item.Brand.Id);
            if (distinctBrands.Count > FullIconPrewarmBrandLimit) break;
        }

        _hasResolvableBrands = distinctBrands.Count > 0;
        _prewarmAllIcons = items.Length <= FullIconPrewarmAccountLimit
            || distinctBrands.Count <= FullIconPrewarmBrandLimit;
    }

    private Task StartInitialIconPrewarm()
    {
        CancelInitialIconPrewarm();
        if (_disposed || _adapter.ItemCount == 0 || !_hasResolvableBrands)
        {
            _brandIcons.SetPinnedRuntimeIcons([]);
            return Task.CompletedTask;
        }

        var items = _adapter.Items;
        var initialAccountCount = Math.Min(items.Length, InitialIconPrewarmAccountCount);
        var pinnedBrandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pinnedBrands = items
            .Take(initialAccountCount)
            .Select(static item => item.Brand)
            .Where(brand => brand.Id is { Length: > 0 } id && pinnedBrandIds.Add(id))
            .ToArray();
        _brandIcons.SetPinnedRuntimeIcons(pinnedBrands);

        var brands = pinnedBrands;
        if (_prewarmAllIcons && initialAccountCount < items.Length)
        {
            var allBrandIds = new HashSet<string>(pinnedBrandIds, StringComparer.OrdinalIgnoreCase);
            brands = items
                .Select(static item => item.Brand)
                .Where(brand => brand.Id is { Length: > 0 } id && allBrandIds.Add(id))
                .Concat(pinnedBrands)
                .ToArray();
        }
        if (brands.Length == 0) return Task.CompletedTask;

        _iconPrewarmRequestKey = _prewarmAllIcons ? -1 : 0;
        _initialIconPrewarmLifetime = new CancellationTokenSource();
        return PrewarmIconsAsync(brands, _initialIconPrewarmLifetime.Token);
    }

    private void ScheduleIconPrewarm()
    {
        if (_disposed || _adapter.ItemCount == 0 || !_hasResolvableBrands) return;

        var items = _adapter.Items;
        var first = _layoutManager.FindFirstVisibleItemPosition();
        var last = _layoutManager.FindLastVisibleItemPosition();
        if (first == NoPosition || last == NoPosition)
        {
            first = 0;
            last = Math.Min(items.Length - 1, 9);
        }

        var currentBatch = Math.Max(0, first) / IconPrewarmBatchSize;
        var lastBatch = (items.Length - 1) / IconPrewarmBatchSize;
        var lookaheadBatch = Math.Min(lastBatch, currentBatch + 1);
        var requestKey = _prewarmAllIcons
            ? -1
            : !_hasAccountListScrolled && currentBatch == 0
                ? 0
                : lookaheadBatch + 1;
        if (requestKey == _iconPrewarmRequestKey) return;

        CancelIconPrewarm();
        _iconPrewarmRequestKey = requestKey;
        var positions = new List<int>(_prewarmAllIcons
            ? items.Length
            : (IconPrewarmBatchSize * 2) + 16);
        var includedPositions = new HashSet<int>();
        void AddPosition(int position)
        {
            if (position >= 0 && position < items.Length && includedPositions.Add(position))
                positions.Add(position);
        }

        void AddBatch(int batch)
        {
            var start = batch * IconPrewarmBatchSize;
            var end = Math.Min(items.Length, start + IconPrewarmBatchSize);
            for (var position = start; position < end; position++) AddPosition(position);
        }

        if (_viewModel.AccountRevealRequest is { } reveal)
            AddPosition(_adapter.FindPosition(reveal.AccountId));
        if (_viewModel.SelectedAccount is { } selected)
            AddPosition(_adapter.FindPosition(selected.Id));

        for (var position = Math.Max(0, first - 2); position <= last; position++)
            AddPosition(position);

        if (_prewarmAllIcons)
        {
            for (var position = 0; position < items.Length; position++) AddPosition(position);
        }
        else if (requestKey == 0)
        {
            for (var position = 0;
                 position < Math.Min(items.Length, InitialIconPrewarmAccountCount);
                 position++)
            {
                AddPosition(position);
            }
        }
        else
        {
            AddBatch(currentBatch);
            if (lookaheadBatch != currentBatch) AddBatch(lookaheadBatch);
        }

        var seenBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brands = positions
            .Select(position => items[position].Brand)
            .Where(brand => brand.Id is { Length: > 0 } id && seenBrands.Add(id))
            .ToArray();
        if (brands.Length == 0) return;

        _iconPrefetchLifetime = new CancellationTokenSource();
        _ = PrewarmIconsAsync(brands, _iconPrefetchLifetime.Token);
    }

    private async Task PrewarmIconsAsync(
        IReadOnlyList<BrandInfo> brands,
        CancellationToken cancellationToken)
    {
        try
        {
            var vectorBrands = new List<BrandInfo>();
            var runtimeBrands = new List<BrandInfo>();
            foreach (var metadata in brands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (metadata.RuntimeIcon is not null)
                {
                    runtimeBrands.Add(metadata);
                    continue;
                }

                BrandInfo materialized;
                try
                {
                    materialized = _viewModel.TryGetMaterializedBrand(metadata, out var cached)
                        ? cached
                        : await Task.Run(
                            () => _viewModel.MaterializeBrand(metadata),
                            cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    continue;
                }
                if (!materialized.HasIcon || materialized.Id is null) continue;

                if (materialized.RuntimeIcon is not null)
                {
                    runtimeBrands.Add(materialized);
                    continue;
                }

                vectorBrands.Add(materialized);
            }

            await Parallel.ForEachAsync(
                runtimeBrands,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = RuntimeIconPrewarmConcurrency
                },
                async (brand, token) =>
                {
                    await _brandIcons.PrewarmRuntimeAsync(brand, token);
                });

            foreach (var materialized in vectorBrands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prepared = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var templateCreated = false;
                Post(() =>
                {
                    try
                    {
                        if (_disposed
                            || cancellationToken.IsCancellationRequested
                            || _adapter.IsListScrolling)
                        {
                            return;
                        }

                        using var target = NativeAccountRowView.CreateBrandIconTarget(Context!);
                        if (_brandIcons.IsPrewarmed(
                                materialized,
                                target,
                                global::Android.Graphics.Color.White))
                        {
                            return;
                        }

                        templateCreated = _brandIcons.Prewarm(
                            materialized,
                            target,
                            global::Android.Graphics.Color.White);
                        if (templateCreated) RefreshAttachedBrandIcon(materialized.Id!);
                    }
                    catch (Exception)
                    {
                        // A malformed optional icon must not stop the remaining prewarm batch.
                    }
                    finally
                    {
                        prepared.TrySetResult();
                    }
                });
                await prepared.Task.WaitAsync(cancellationToken);
                if (templateCreated) await Task.Delay(8, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Scrolling or a new list projection superseded this bounded prewarm pass.
        }
        catch (Exception)
        {
            // Icons are optional; initials remain available for every account row.
        }
    }

    private void RefreshAttachedBrandIcon(string brandId)
    {
        for (var index = 0; index < ChildCount; index++)
        {
            var child = GetChildAt(index);
            if (child is not null
                && GetChildViewHolder(child) is NativeAccountAdapter.AccountViewHolder holder)
            {
                holder.Row.RefreshBrandIcon(brandId);
            }
        }
    }

    private void RuntimeIconReady(string brandId)
    {
        if (_disposed) return;
        lock (_pendingRuntimeIconRefreshLock)
        {
            _pendingRuntimeIconRefreshes.Add(brandId);
            if (_runtimeIconRefreshPosted) return;
            _runtimeIconRefreshPosted = true;
        }

        Post(FlushRuntimeIconRefreshes);
    }

    private void FlushRuntimeIconRefreshes()
    {
        string[] brandIds;
        lock (_pendingRuntimeIconRefreshLock)
        {
            brandIds = [.. _pendingRuntimeIconRefreshes];
            _pendingRuntimeIconRefreshes.Clear();
            _runtimeIconRefreshPosted = false;
        }

        if (_disposed) return;
        foreach (var brandId in brandIds) RefreshAttachedBrandIcon(brandId);
    }

    private void CancelIconPrewarm()
    {
        var lifetime = _iconPrefetchLifetime;
        _iconPrefetchLifetime = null;
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private void CancelInitialIconPrewarm()
    {
        var lifetime = _initialIconPrewarmLifetime;
        _initialIconPrewarmLifetime = null;
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private void CancelAllIconPrewarm()
    {
        CancelIconPrewarm();
        CancelInitialIconPrewarm();
    }

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
                owner.HandleIdleScroll();
            else
                owner.ScheduleIconPrewarm();
        }
    }
}

internal sealed class NativeAccountAdapter : RecyclerView.Adapter
{
    internal const int AccountViewType = 1;
    private readonly MobileShellViewModel _viewModel;
    private readonly NativeBrandIconCache _brandIcons;
    private NativeAccountRow[] _items = [];
    private NativeAccountRowView? _openRow;
    private bool _isListScrolling;

    public NativeAccountAdapter(
        MobileShellViewModel viewModel,
        NativeBrandIconCache brandIcons)
    {
        _viewModel = viewModel;
        _brandIcons = brandIcons;
        HasStableIds = true;
    }

    public NativeAccountRow[] Items => _items;
    public bool IsListScrolling => _isListScrolling;
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
        var row = new NativeAccountRowView(
            parent.Context!,
            _viewModel,
            _brandIcons,
            OpenRow);
        row.LayoutParameters = new RecyclerView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            NativeAccountRowView.RowHeight(parent.Context!));
        return new AccountViewHolder(row);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) =>
        ((AccountViewHolder)holder).Bind(_items[position], _isListScrolling);

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
        if (holder is AccountViewHolder accountHolder)
        {
            accountHolder.Row.SetListScrolling(_isListScrolling);
            accountHolder.Row.StartAnimating();
        }
    }

    internal void SetListScrolling(bool isScrolling) =>
        _isListScrolling = isScrolling;

    internal void ReplaceAll(NativeAccountRow[] items)
    {
        CloseOpenRow();
        _items = items;
        NotifyDataSetChanged();
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
        public void Bind(NativeAccountRow item, bool isListScrolling) =>
            Row.Bind(item, isListScrolling);
        public void Unbind() => Row.Unbind();
    }
}

internal sealed record NativeAccountRow(
    Guid Id,
    MobileAccountItem Account,
    string Issuer,
    string AccountName,
    string CustomPeriodLabel,
    TOTP.Avalonia.Shared.Branding.BrandInfo Brand,
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
        account.Brand,
        account.Brand.Initials,
        account.Brand.BackgroundColor,
        account.Brand.IconData,
        account.ShowIssuerLogo,
        account.IsFavorite);
}
