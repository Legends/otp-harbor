using System.Security.Cryptography;
using FluentResults;
using TOTP.Core.Icons;

namespace TOTP.Infrastructure.Icons;

public sealed class CustomIconImporter : ICustomIconImporter
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly SvgIconImporter _svgImporter = new();

    public async Task<Result<ImportedIcon>> ImportAsync(
        CustomIconSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var data = await ReadBoundedAsync(source.Stream, cancellationToken);
            if (data.Length == 0)
                return Failure(CustomIconImportFailureReason.Empty);

            var extension = Path.GetExtension(source.FileName ?? string.Empty);
            var isPng = data.AsSpan().StartsWith(PngSignature);
            if (isPng || extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                return ImportPng(source, data, isPng);

            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                || StartsWithXml(data))
            {
                await using var svg = new MemoryStream(data, writable: false);
                return await _svgImporter.ImportAsync(
                    new CustomIconSource
                    {
                        Stream = svg,
                        FileName = source.FileName,
                        DisplayName = source.DisplayName,
                        Issuers = source.Issuers
                    },
                    cancellationToken);
            }

            return Failure(CustomIconImportFailureReason.UnsupportedFormat);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CustomIconTooLargeException)
        {
            return Failure(CustomIconImportFailureReason.TooLarge);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(CustomIconImportFailureReason.Unreadable);
        }
    }

    private static Result<ImportedIcon> ImportPng(
        CustomIconSource source,
        byte[] data,
        bool hasPngSignature)
    {
        if (!hasPngSignature)
            return Failure(CustomIconImportFailureReason.InvalidPng);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            RuntimePngProfileValidator.ValidateCustomEncoded(data, hash);
            var sourceFileName = SafeSourceFileName(source.FileName);
            var name = SafeDisplayName(source, sourceFileName);
            var aliases = source.Issuers
                .Where(value => IconImportArchive.IsSafeDisplayText(value, 128))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(IconImportArchive.MaximumAliasesPerIcon)
                .ToArray();
            return Result.Ok(new ImportedIcon(
                $"custom_{hash[..24]}",
                name,
                null,
                aliases,
                "custom-png",
                "#334155",
                sourceFileName,
                PngData: data));
        }
        catch (InvalidDataException)
        {
            return Failure(CustomIconImportFailureReason.InvalidPng);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > RuntimePngProfileValidator.MaximumEncodedBytes)
                throw new CustomIconTooLargeException();
            output.Write(buffer, 0, read);
        }
    }

    private static bool StartsWithXml(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        if (data.StartsWith([(byte)0xef, (byte)0xbb, (byte)0xbf])) offset = 3;
        while (offset < data.Length && char.IsWhiteSpace((char)data[offset])) offset++;
        return offset < data.Length && data[offset] == '<';
    }

    private static string? SafeSourceFileName(string? fileName)
    {
        var sourceFileName = Path.GetFileName(fileName ?? string.Empty);
        return IconImportArchive.IsSafeDisplayText(sourceFileName, 256)
            ? sourceFileName
            : null;
    }

    private static string SafeDisplayName(CustomIconSource source, string? sourceFileName)
    {
        var fileStem = Path.GetFileNameWithoutExtension(sourceFileName ?? string.Empty);
        return IconImportArchive.IsSafeDisplayText(source.DisplayName)
            ? source.DisplayName!.Trim()
            : IconImportArchive.IsSafeDisplayText(fileStem)
                ? fileStem
                : "Custom icon";
    }

    private static Result<ImportedIcon> Failure(CustomIconImportFailureReason reason) =>
        Result.Fail<ImportedIcon>(new CustomIconImportError(reason));

    private sealed class CustomIconTooLargeException : Exception
    {
    }
}
