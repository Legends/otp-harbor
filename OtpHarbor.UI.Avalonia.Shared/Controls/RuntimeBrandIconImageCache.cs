using Avalonia.Media;
using Avalonia.Media.Imaging;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Controls;

internal static class RuntimeBrandIconImageCache
{
    private const int MaximumEntries = 128;
    private const long MaximumBytes = 8L * 1024 * 1024;
    private const int RuntimeWidth = 128;
    private const int RuntimeHeight = 128;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Task<bool>> Loads = new(StringComparer.Ordinal);
    private static long _bytes;
    private static long _accessSequence;

    internal static async Task<Lease?> AcquireAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationToken cancellationToken)
    {
        Task<bool> load;
        lock (Gate)
        {
            if (Entries.TryGetValue(runtimeIcon.CacheKey, out var cached))
            {
                cached.LastAccess = ++_accessSequence;
                return cached.Acquire();
            }

            if (!Loads.TryGetValue(runtimeIcon.CacheKey, out load!))
            {
                load = Task.Run(
                    () => LoadAndCacheAsync(runtimeIcon),
                    CancellationToken.None);
                Loads.Add(runtimeIcon.CacheKey, load);
            }
        }

        if (!await load.WaitAsync(cancellationToken).ConfigureAwait(false)) return null;
        lock (Gate)
        {
            if (!Entries.TryGetValue(runtimeIcon.CacheKey, out var cached)) return null;
            cached.LastAccess = ++_accessSequence;
            return cached.Acquire();
        }
    }

    private static async Task<bool> LoadAndCacheAsync(BrandRuntimeIcon runtimeIcon)
    {
        Bitmap? bitmap = null;
        try
        {
            var data = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
                    runtimeIcon,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (data is null) return false;

            using var stream = new MemoryStream(data, writable: false);
            bitmap = new Bitmap(stream);
            if (runtimeIcon.Width != RuntimeWidth
                || runtimeIcon.Height != RuntimeHeight)
                return false;

            var entry = new Entry(bitmap, RuntimeWidth * RuntimeHeight * 4L);
            bitmap = null;
            lock (Gate)
            {
                entry.LastAccess = ++_accessSequence;
                Entries.Add(runtimeIcon.CacheKey, entry);
                _bytes += entry.ByteCount;
                EvictEntries();
            }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            bitmap?.Dispose();
            lock (Gate) Loads.Remove(runtimeIcon.CacheKey);
        }
    }

    private static void EvictEntries()
    {
        while (Entries.Count > MaximumEntries || _bytes > MaximumBytes)
        {
            var oldest = Entries.MinBy(static pair => pair.Value.LastAccess);
            if (oldest.Key is null) return;
            Entries.Remove(oldest.Key);
            _bytes -= oldest.Value.ByteCount;
            oldest.Value.ReleaseCacheOwnership();
        }
    }

    internal sealed class Lease : IDisposable
    {
        private Entry? _entry;

        internal Lease(Entry entry)
        {
            _entry = entry;
            Image = entry.Bitmap;
        }

        internal IImage Image { get; }

        public void Dispose() => Interlocked.Exchange(ref _entry, null)?.Release();
    }

    internal sealed class Entry(Bitmap bitmap, long byteCount)
    {
        private int _references = 1;

        internal Bitmap Bitmap { get; } = bitmap;
        internal long ByteCount { get; } = byteCount;
        internal long LastAccess { get; set; }

        internal Lease Acquire()
        {
            Interlocked.Increment(ref _references);
            return new Lease(this);
        }

        internal void ReleaseCacheOwnership() => Release();

        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0) Bitmap.Dispose();
        }
    }
}
