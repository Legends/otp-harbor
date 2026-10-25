using Android.Graphics;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Android;

internal sealed class NativeBrandIconCache : IDisposable
{
    private const int MaximumRuntimeEntries = 640;
    private const long MaximumRuntimeBytes = 48L * 1024 * 1024;
    private const int MaximumEncodedRuntimeBytes = 1024 * 1024;
    private const int RuntimeWidth = 128;
    private const int RuntimeHeight = 128;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeEntry> _runtimeEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<bool>> _runtimeLoads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pinnedRuntimeKeys = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _runtimeLock = new();
    private long _runtimeBytes;
    private long _accessSequence;
    private bool _disposed;

    internal event Action<string>? RuntimeIconReady;

    public NativeBrandIcon? TryCreate(
        BrandInfo brand,
        RectF target,
        Color monochromeColor,
        bool allowTemplateCreation,
        bool requireRuntimeIconOnFirstDraw = false)
    {
        if (string.IsNullOrWhiteSpace(brand.Id) || !brand.HasIcon) return null;

        if (brand.RuntimeIcon is { } runtimeIcon)
        {
            var cached = TryAcquireRuntime(runtimeIcon, target);
            if (cached is not null) return cached;
            if (requireRuntimeIconOnFirstDraw)
            {
                var loaded = LoadRuntimeAsync(runtimeIcon, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                return loaded ? TryAcquireRuntime(runtimeIcon, target) : null;
            }
            EnsureRuntimeLoad(runtimeIcon);
            return null;
        }

        if (_entries.TryGetValue(brand.Id, out var existing)
            && EntryMatches(existing, brand, target, monochromeColor))
        {
            return existing.Template.Clone();
        }

        if (!allowTemplateCreation) return null;
        return ReplaceTemplate(brand, target, monochromeColor)?.Clone();
    }

    public bool Prewarm(BrandInfo brand, RectF target, Color monochromeColor)
    {
        if (string.IsNullOrWhiteSpace(brand.Id) || !brand.HasIcon) return false;
        if (brand.RuntimeIcon is { } runtimeIcon)
        {
            EnsureRuntimeLoad(runtimeIcon);
            return IsRuntimeCached(runtimeIcon.CacheKey);
        }
        if (IsPrewarmed(brand, target, monochromeColor)) return true;

        return ReplaceTemplate(brand, target, monochromeColor) is not null;
    }

    internal Task<bool> PrewarmRuntimeAsync(
        BrandInfo brand,
        CancellationToken cancellationToken) => brand.RuntimeIcon is { } runtimeIcon
        ? LoadRuntimeAsync(runtimeIcon, cancellationToken)
        : Task.FromResult(false);

    internal void SetPinnedRuntimeIcons(IEnumerable<BrandInfo> brands)
    {
        ArgumentNullException.ThrowIfNull(brands);
        var cacheKeys = brands
            .Select(static brand => brand.RuntimeIcon?.CacheKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key!)
            .ToHashSet(StringComparer.Ordinal);

        lock (_runtimeLock)
        {
            if (_disposed) return;
            _pinnedRuntimeKeys.Clear();
            _pinnedRuntimeKeys.UnionWith(cacheKeys);
            EvictRuntimeEntries();
        }
    }

    public bool IsPrewarmed(BrandInfo brand, RectF target, Color monochromeColor)
    {
        if (brand.RuntimeIcon is { } runtimeIcon) return IsRuntimeCached(runtimeIcon.CacheKey);
        return !string.IsNullOrWhiteSpace(brand.Id)
            && brand.HasIcon
            && _entries.TryGetValue(brand.Id, out var existing)
            && EntryMatches(existing, brand, target, monochromeColor);
    }

    private NativeBrandIcon? TryAcquireRuntime(BrandRuntimeIcon runtimeIcon, RectF target)
    {
        lock (_runtimeLock)
        {
            if (_disposed
                || !_runtimeEntries.TryGetValue(runtimeIcon.CacheKey, out var entry))
                return null;
            entry.LastAccess = ++_accessSequence;
            return entry.Acquire(target);
        }
    }

    private bool IsRuntimeCached(string cacheKey)
    {
        lock (_runtimeLock) return _runtimeEntries.ContainsKey(cacheKey);
    }

    private void EnsureRuntimeLoad(BrandRuntimeIcon runtimeIcon) =>
        _ = LoadRuntimeAsync(runtimeIcon, CancellationToken.None);

    private async Task<bool> LoadRuntimeAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationToken cancellationToken)
    {
        Task<bool> load;
        lock (_runtimeLock)
        {
            if (_disposed) return false;
            if (_runtimeEntries.TryGetValue(runtimeIcon.CacheKey, out var existing))
            {
                existing.LastAccess = ++_accessSequence;
                return true;
            }
            if (!_runtimeLoads.TryGetValue(runtimeIcon.CacheKey, out load!))
            {
                load = Task.Run(
                    () => LoadAndCacheRuntimeAsync(runtimeIcon, _lifetime.Token),
                    CancellationToken.None);
                _runtimeLoads.Add(runtimeIcon.CacheKey, load);
            }
        }

        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task<bool> LoadAndCacheRuntimeAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encoded = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
                    runtimeIcon,
                    cancellationToken)
                .ConfigureAwait(false);
            if (encoded is null || encoded.Length > MaximumEncodedRuntimeBytes) return false;
            using var options = new BitmapFactory.Options
            {
                InPreferredConfig = Bitmap.Config.Argb8888,
                InScaled = false
            };
            bitmap = BitmapFactory.DecodeByteArray(encoded, 0, encoded.Length, options);
            if (bitmap is null
                || bitmap.Width != RuntimeWidth
                || bitmap.Height != RuntimeHeight
                || runtimeIcon.Width != RuntimeWidth
                || runtimeIcon.Height != RuntimeHeight)
                return false;

            var entry = new RuntimeEntry(bitmap, RuntimeWidth * RuntimeHeight * 4L);
            bitmap = null;
            lock (_runtimeLock)
            {
                if (_disposed)
                {
                    entry.ReleaseCacheOwnership();
                    return false;
                }
                if (_runtimeEntries.TryGetValue(runtimeIcon.CacheKey, out var duplicate))
                {
                    duplicate.LastAccess = ++_accessSequence;
                    entry.ReleaseCacheOwnership();
                }
                else
                {
                    entry.LastAccess = ++_accessSequence;
                    _runtimeEntries.Add(runtimeIcon.CacheKey, entry);
                    _runtimeBytes += entry.ByteCount;
                    EvictRuntimeEntries();
                }
            }

            var separator = runtimeIcon.CacheKey.LastIndexOf(':');
            if (separator > 0) RuntimeIconReady?.Invoke(runtimeIcon.CacheKey[..separator]);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            bitmap?.Dispose();
            lock (_runtimeLock) _runtimeLoads.Remove(runtimeIcon.CacheKey);
        }
    }

    private void EvictRuntimeEntries()
    {
        while (_runtimeEntries.Count > MaximumRuntimeEntries
               || _runtimeBytes > MaximumRuntimeBytes)
        {
            var oldest = _runtimeEntries
                .Where(pair => !_pinnedRuntimeKeys.Contains(pair.Key))
                .OrderBy(static pair => pair.Value.LastAccess)
                .FirstOrDefault();
            if (oldest.Key is null) break;
            _runtimeEntries.Remove(oldest.Key);
            _runtimeBytes -= oldest.Value.ByteCount;
            oldest.Value.ReleaseCacheOwnership();
        }
    }

    private static bool EntryMatches(
        Entry existing,
        BrandInfo brand,
        RectF target,
        Color monochromeColor) =>
        IconPayloadMatches(existing, brand)
        && existing.TargetLeft.Equals(target.Left)
        && existing.TargetTop.Equals(target.Top)
        && existing.TargetWidth.Equals(target.Width())
        && existing.TargetHeight.Equals(target.Height())
        && existing.MonochromeColor == monochromeColor.ToArgb();

    private static bool IconPayloadMatches(Entry existing, BrandInfo brand)
    {
        if (!string.Equals(existing.IconData, brand.IconData, StringComparison.Ordinal)
            || !Equals(existing.SourceTransform, brand.SourceTransform))
        {
            return false;
        }

        if (ReferenceEquals(existing.IconLayers, brand.IconLayers)) return true;
        return existing.IconLayers is not null
            && brand.IconLayers is not null
            && existing.IconLayers.SequenceEqual(brand.IconLayers);
    }

    private NativeBrandIcon? ReplaceTemplate(
        BrandInfo brand,
        RectF target,
        Color monochromeColor)
    {
        var template = NativeBrandIcon.TryCreate(brand, target, monochromeColor);
        if (template is null) return null;

        if (_entries.Remove(brand.Id!, out var previous)) previous.Template.Dispose();
        _entries[brand.Id!] = new Entry(
            brand.IconData,
            brand.IconLayers,
            brand.SourceTransform,
            target.Left,
            target.Top,
            target.Width(),
            target.Height(),
            monochromeColor.ToArgb(),
            template);
        return template;
    }

    public void Dispose()
    {
        lock (_runtimeLock)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            foreach (var entry in _runtimeEntries.Values) entry.ReleaseCacheOwnership();
            _runtimeEntries.Clear();
            _pinnedRuntimeKeys.Clear();
            _runtimeBytes = 0;
        }
        foreach (var entry in _entries.Values) entry.Template.Dispose();
        _entries.Clear();
        _lifetime.Dispose();
    }

    private sealed record Entry(
        string? IconData,
        IReadOnlyList<BrandIconLayer>? IconLayers,
        BrandIconTransform? SourceTransform,
        float TargetLeft,
        float TargetTop,
        float TargetWidth,
        float TargetHeight,
        int MonochromeColor,
        NativeBrandIcon Template);

    private sealed class RuntimeEntry(Bitmap bitmap, long byteCount)
    {
        private int _references = 1;

        internal long ByteCount { get; } = byteCount;
        internal long LastAccess { get; set; }

        internal NativeBrandIcon Acquire(RectF target)
        {
            Interlocked.Increment(ref _references);
            return NativeBrandIcon.FromBitmap(bitmap, target, Release);
        }

        internal void ReleaseCacheOwnership() => Release();

        private void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0) bitmap.Dispose();
        }
    }
}
