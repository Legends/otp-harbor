using System.IO.Compression;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Controls;

public static class RuntimeBrandIconAssetReader
{
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;
    private const int MaximumCachedArchives = 4;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CachedArchive> Archives =
        new(StringComparer.Ordinal);
    private static long _accessSequence;

    public static async Task<byte[]?> ReadValidatedAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeIcon);
        if (runtimeIcon.Width != RuntimePngProfileValidator.Width
            || runtimeIcon.Height != RuntimePngProfileValidator.Height
            || !HasCanonicalCacheKey(runtimeIcon))
            return null;

        try
        {
            byte[]? encoded;
            if (runtimeIcon.IsArchiveBacked)
            {
                if (!HasCanonicalArchiveEntry(runtimeIcon)) return null;
                encoded = await ReadArchiveEntryAsync(runtimeIcon, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                encoded = await ReadLegacyFileAsync(runtimeIcon, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (encoded is null) return null;
            if (!runtimeIcon.IsArchiveBacked
                && runtimeIcon.BrandId?.StartsWith("custom_", StringComparison.Ordinal) == true)
                RuntimePngProfileValidator.ValidateCustomEncoded(encoded, runtimeIcon.Sha256);
            else
                RuntimePngProfileValidator.ValidateEncoded(encoded, runtimeIcon.Sha256);
            return encoded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasCanonicalCacheKey(BrandRuntimeIcon runtimeIcon)
    {
        if (runtimeIcon.BrandId is null) return true;
        var expected = runtimeIcon.Theme switch
        {
            BrandIconTheme.Light => $"{runtimeIcon.BrandId}:light:{runtimeIcon.Sha256}",
            BrandIconTheme.Dark => $"{runtimeIcon.BrandId}:dark:{runtimeIcon.Sha256}",
            _ => $"{runtimeIcon.BrandId}:{runtimeIcon.Sha256}"
        };
        return string.Equals(runtimeIcon.CacheKey, expected, StringComparison.Ordinal);
    }

    private static bool HasCanonicalArchiveEntry(BrandRuntimeIcon runtimeIcon)
    {
        var entryPath = runtimeIcon.ArchiveEntryPath!;
        if (entryPath.Length == 0
            || entryPath.Contains('\\')
            || entryPath[0] == '/'
            || Path.IsPathRooted(entryPath)
            || entryPath.Split('/').Any(static segment => segment is "" or "." or ".."))
            return false;
        if (runtimeIcon.BrandId is null) return false;
        var expected = runtimeIcon.Theme is null
            ? $"runtime/foreground-128/{runtimeIcon.BrandId}.png"
            : $"runtime/appearance-128/{runtimeIcon.Sha256}.png";
        return string.Equals(entryPath, expected, StringComparison.Ordinal);
    }

    private static async Task<byte[]?> ReadArchiveEntryAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.GetFullPath(runtimeIcon.ArchivePath!);
        ArchiveHandle handle;
        CachedArchive? cached = null;
        var ownsHandle = false;
        lock (Gate)
        {
            if (Archives.TryGetValue(archivePath, out cached))
            {
                cached.ActiveReaders++;
                cached.LastAccess = ++_accessSequence;
                handle = cached.Handle;
            }
            else
            {
                if (Archives.Count >= MaximumCachedArchives)
                {
                    var eviction = Archives
                        .Where(static pair => pair.Value.ActiveReaders == 0)
                        .OrderBy(static pair => pair.Value.LastAccess)
                        .FirstOrDefault();
                    if (eviction.Key is not null)
                    {
                        Archives.Remove(eviction.Key);
                        eviction.Value.Handle.Dispose();
                    }
                }

                handle = ArchiveHandle.Open(archivePath);
                if (Archives.Count < MaximumCachedArchives)
                {
                    cached = new CachedArchive(handle)
                    {
                        ActiveReaders = 1,
                        LastAccess = ++_accessSequence
                    };
                    Archives.Add(archivePath, cached);
                }
                else
                    ownsHandle = true;
            }
        }

        try
        {
            return await handle.ReadAsync(runtimeIcon.ArchiveEntryPath!, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (ownsHandle)
            {
                handle.Dispose();
            }
            else
            {
                lock (Gate) cached!.ActiveReaders--;
            }
        }
    }

    private static async Task<byte[]?> ReadLegacyFileAsync(
        BrandRuntimeIcon runtimeIcon,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeIcon.FilePath)) return null;
        var file = new FileInfo(runtimeIcon.FilePath);
        if (!file.Exists
            || file.Length is <= 0 or > RuntimePngProfileValidator.MaximumEncodedBytes)
            return null;
        await using var input = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            16 * 1024,
            useAsync: true);
        return await ReadBoundedAsync(input, file.Length, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream input,
        long declaredLength,
        CancellationToken cancellationToken)
    {
        if (declaredLength is <= 0 or > RuntimePngProfileValidator.MaximumEncodedBytes)
            return null;
        var data = GC.AllocateUninitializedArray<byte>((int)declaredLength);
        var offset = 0;
        while (offset < data.Length)
        {
            var read = await input.ReadAsync(data.AsMemory(offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) return null;
            offset += read;
        }
        return await input.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) == 0
            ? data
            : null;
    }

    private sealed class ArchiveHandle : IDisposable
    {
        private readonly FileStream _stream;
        private readonly ZipArchive _archive;
        private readonly IReadOnlyDictionary<string, ZipArchiveEntry> _entries;
        private readonly SemaphoreSlim _readLock = new(1, 1);

        private ArchiveHandle(
            FileStream stream,
            ZipArchive archive,
            IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            _stream = stream;
            _archive = archive;
            _entries = entries;
        }

        internal static ArchiveHandle Open(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumArchiveBytes)
                throw new InvalidDataException("The installed runtime icon archive has an invalid size.");
            FileStream? stream = null;
            ZipArchive? archive = null;
            try
            {
                stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                foreach (var entry in archive.Entries)
                {
                    if (!entries.TryAdd(entry.FullName, entry))
                        throw new InvalidDataException("The installed runtime icon archive has duplicate entries.");
                }
                return new ArchiveHandle(stream, archive, entries);
            }
            catch
            {
                archive?.Dispose();
                stream?.Dispose();
                throw;
            }
        }

        internal async Task<byte[]?> ReadAsync(
            string entryPath,
            CancellationToken cancellationToken)
        {
            await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_entries.TryGetValue(entryPath, out var entry)
                    || !string.Equals(entry.FullName, entryPath, StringComparison.Ordinal)
                    || entry.Length is <= 0 or > RuntimePngProfileValidator.MaximumEncodedBytes)
                    return null;
                await using var input = entry.Open();
                return await ReadBoundedAsync(input, entry.Length, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _readLock.Release();
            }
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
            _readLock.Dispose();
        }
    }

    private sealed class CachedArchive(ArchiveHandle handle)
    {
        internal ArchiveHandle Handle { get; } = handle;
        internal int ActiveReaders { get; set; }
        internal long LastAccess { get; set; }
    }
}
