using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using TOTP.Core.Icons;

namespace TOTP.Infrastructure.Icons;

internal static partial class IconImportArchive
{
    internal const int MaximumEntries = 100_000;
    internal const long MaximumExpandedBytes = 512L * 1024 * 1024;
    internal const int MaximumCompressionRatio = 200;
    internal const int MaximumMetadataBytes = 128 * 1024 * 1024;
    internal const int MaximumSvgBytes = 1024 * 1024;
    internal const int MaximumIcons = 100_000;
    internal const int MaximumAliasesPerIcon = 32;
    internal const int MaximumNotices = 64;
    internal const int MaximumNoticeBytes = 128 * 1024;

    internal static ZipArchive Open(IconPackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Stream);
        if (!source.Stream.CanRead || !source.Stream.CanSeek)
            throw new InvalidDataException("Icon pack streams must be readable and seekable.");
        source.Stream.Position = 0;
        var archive = new ZipArchive(source.Stream, ZipArchiveMode.Read, leaveOpen: true);
        ValidateShape(archive);
        return archive;
    }

    internal static void ValidateShape(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumEntries)
            throw new InvalidDataException("Unexpected icon archive entry count.");

        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeEntryName(entry.FullName);
            if (!names.Add(normalized))
                throw new InvalidDataException("Duplicate icon archive entry.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes)
                throw new InvalidDataException("Expanded icon archive is too large.");
            if (entry.Length > 0
                && (entry.CompressedLength <= 0
                    || entry.Length / Math.Max(1d, entry.CompressedLength) > MaximumCompressionRatio))
                throw new InvalidDataException("Icon archive compression ratio is unsafe.");
            EnsureSafeRelativePath(normalized);
        }
    }

    internal static ZipArchiveEntry? FindUniqueEntryBySuffix(
        ZipArchive archive,
        string suffix,
        int maximumBytes)
    {
        var normalizedSuffix = suffix.Replace('\\', '/');
        var matches = archive.Entries.Where(entry =>
            MatchesSuffix(entry, normalizedSuffix)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || matches[0].Length is <= 0 || matches[0].Length > maximumBytes)
            throw new InvalidDataException("Icon pack metadata is invalid or ambiguous.");
        return matches[0];
    }

    internal static bool ContainsEntryBySuffix(ZipArchive archive, string suffix)
    {
        var normalizedSuffix = suffix.Replace('\\', '/');
        return archive.Entries.Any(entry => MatchesSuffix(entry, normalizedSuffix));
    }

    internal static string PrefixBefore(ZipArchiveEntry entry, string suffix) =>
        NormalizeEntryName(entry.FullName)[..^suffix.Length];

    internal static ZipArchiveEntry? FindEntryByPath(ZipArchive archive, string path)
    {
        var normalizedPath = NormalizeEntryName(path);
        EnsureSafeRelativePath(normalizedPath);
        return archive.Entries.SingleOrDefault(entry =>
            NormalizeEntryName(entry.FullName).Equals(
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<byte[]> ReadValidatedSvgAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
        => (await ReadValidatedSvgWithMetadataAsync(entry, cancellationToken)).Data;

    internal static async Task<ValidatedSvg> ReadValidatedSvgWithMetadataAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Length <= 0)
            throw new SvgValidationException(
                CustomIconImportFailureReason.Empty,
                "An SVG is empty.");
        if (entry.Length > MaximumSvgBytes)
            throw new SvgValidationException(
                CustomIconImportFailureReason.TooLarge,
                "An SVG exceeds the supported size.");
        await using var input = entry.Open();
        return await ReadValidatedSvgWithMetadataAsync(input, cancellationToken);
    }

    internal static async Task<byte[]> ReadValidatedSvgAsync(
        Stream stream,
        CancellationToken cancellationToken)
        => (await ReadValidatedSvgWithMetadataAsync(stream, cancellationToken)).Data;

    private static async Task<ValidatedSvg> ReadValidatedSvgWithMetadataAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumSvgBytes)
                throw new SvgValidationException(
                    CustomIconImportFailureReason.TooLarge,
                    "An SVG exceeds the supported size.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (memory.Length == 0)
            throw new SvgValidationException(
                CustomIconImportFailureReason.Empty,
                "An SVG is empty.");

        memory.Position = 0;
        string? backgroundColor = null;
        using (var reader = XmlReader.Create(memory, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumSvgBytes,
            IgnoreComments = true
        }))
        {
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element
                || !reader.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
                throw new SvgValidationException(
                    CustomIconImportFailureReason.MalformedXml,
                    "The SVG root element is invalid.");

            var foundPath = false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var localReferences = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(reader.GetAttribute("d")))
                    foundPath = true;
                if (backgroundColor is null
                    && reader.LocalName.Equals("circle", StringComparison.OrdinalIgnoreCase)
                    && reader.GetAttribute("fill") is { } fill
                    && fill.StartsWith('#')
                    && IsHexColor(fill[1..]))
                {
                    backgroundColor = fill.ToUpperInvariant();
                }
                if (reader.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase)
                    || reader.LocalName.Equals("foreignObject", StringComparison.OrdinalIgnoreCase))
                    throw new SvgValidationException(
                        CustomIconImportFailureReason.UnsafeContent,
                        "External or executable SVG content is not supported.");

                var id = reader.GetAttribute("id");
                if (id is { Length: > 0 } && !ids.Add(id))
                    throw new SvgValidationException(
                        CustomIconImportFailureReason.MalformedXml,
                        "The SVG contains duplicate identifiers.");
                ValidateSvgReference(reader.GetAttribute("href"), localReferences);
                ValidateSvgReference(
                    reader.GetAttribute("href", "http://www.w3.org/1999/xlink"),
                    localReferences);
            } while (reader.Read());

            if (!foundPath)
                throw new SvgValidationException(
                    CustomIconImportFailureReason.MissingVectorPath,
                    "Unsupported SVG content.");
            if (localReferences.Any(reference => !ids.Contains(reference)))
                throw new SvgValidationException(
                    CustomIconImportFailureReason.MalformedXml,
                    "The SVG contains an unresolved local reference.");
        }
        return new ValidatedSvg(memory.ToArray(), backgroundColor);
    }

    private static void ValidateSvgReference(string? value, ISet<string> localReferences)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!SafeLocalSvgReferenceRegex().IsMatch(value))
            throw new SvgValidationException(
                CustomIconImportFailureReason.UnsafeContent,
                "External or executable SVG content is not supported.");
        localReferences.Add(value[1..]);
    }

    internal static async Task<byte[]> ReadBoundedAsync(
        ZipArchiveEntry entry,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length is <= 0 || entry.Length > maximumBytes)
            throw new InvalidDataException("An icon pack entry has an invalid size.");
        await using var input = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        await input.CopyToAsync(memory, cancellationToken);
        if (memory.Length != entry.Length || memory.Length > maximumBytes)
            throw new InvalidDataException("An icon pack entry has an invalid size.");
        return memory.ToArray();
    }

    internal static async Task<IReadOnlyList<ImportedIconNotice>> ReadNoticesAsync(
        ZipArchive archive,
        string prefix,
        CancellationToken cancellationToken)
    {
        var notices = archive.Entries.Where(entry =>
            entry.Name.Length > 0
            && NormalizeEntryName(entry.FullName).StartsWith(prefix, StringComparison.Ordinal)
            && NoticeNameRegex().IsMatch(entry.Name)).ToArray();
        if (notices.Length > MaximumNotices)
            throw new InvalidDataException("The icon pack contains too many notice files.");

        var result = new List<ImportedIconNotice>(notices.Length);
        foreach (var entry in notices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new ImportedIconNotice(
                entry.Name.ToUpperInvariant(),
                await ReadBoundedAsync(entry, MaximumNoticeBytes, cancellationToken)));
        }
        return result;
    }

    internal static string NormalizeEntryName(string value) => value.Replace('\\', '/');

    private static bool MatchesSuffix(ZipArchiveEntry entry, string normalizedSuffix)
    {
        var name = NormalizeEntryName(entry.FullName);
        return name.Equals(normalizedSuffix, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith('/' + normalizedSuffix, StringComparison.OrdinalIgnoreCase);
    }

    internal static void EnsureSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('\0')
            || value.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(value)
            || value.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidDataException("Unsafe icon archive path.");
    }

    internal static bool IsSafeDisplayText(string? value, int maximumLength = 256) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && !value.Any(char.IsControl);

    internal static string ToCanonicalId(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9' || character == '_')
                result.Append(character);
            else if (character == '+') result.Append("plus");
            else if (character == '&') result.Append("and");
        }
        return result.ToString();
    }

    internal static bool IsCanonicalId(string value) => CanonicalIdRegex().IsMatch(value);

    internal static bool IsHexColor(string value) => HexRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalIdRegex();

    [GeneratedRegex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    [GeneratedRegex("^#[A-Za-z_][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeLocalSvgReferenceRegex();

    [GeneratedRegex("^(?:LICENSE|LICENCE|COPYING|NOTICE|DISCLAIMER)(?:\\.(?:MD|TXT))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoticeNameRegex();

    internal sealed class SvgValidationException(
        CustomIconImportFailureReason reason,
        string message) : Exception(message)
    {
        internal CustomIconImportFailureReason Reason { get; } = reason;
    }

    internal sealed record ValidatedSvg(byte[] Data, string? BackgroundColor);
}
