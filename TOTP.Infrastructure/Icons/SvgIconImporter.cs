using System.Security.Cryptography;
using FluentResults;
using TOTP.Core.Icons;

namespace TOTP.Infrastructure.Icons;

public sealed class SvgIconImporter : ICustomIconImporter
{
    public async Task<Result<ImportedIcon>> ImportAsync(
        CustomIconSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var data = await IconImportArchive.ReadValidatedSvgAsync(
                source.Stream,
                cancellationToken);
            var fileStem = Path.GetFileNameWithoutExtension(source.FileName ?? string.Empty);
            var sourceFileName = Path.GetFileName(source.FileName ?? string.Empty);
            if (!IconImportArchive.IsSafeDisplayText(sourceFileName, 256))
                sourceFileName = null;
            var name = IconImportArchive.IsSafeDisplayText(source.DisplayName)
                ? source.DisplayName!.Trim()
                : IconImportArchive.IsSafeDisplayText(fileStem)
                    ? fileStem
                    : "Custom icon";
            var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var id = $"custom_{hash[..24]}";
            var aliases = source.Issuers
                .Where(value => IconImportArchive.IsSafeDisplayText(value, 128))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(IconImportArchive.MaximumAliasesPerIcon)
                .ToArray();
            return Result.Ok(new ImportedIcon(
                id,
                name,
                data,
                aliases,
                "custom-svg",
                "#334155",
                sourceFileName));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IconImportArchive.SvgValidationException ex)
        {
            return Result.Fail<ImportedIcon>(new CustomIconImportError(ex.Reason));
        }
        catch (System.Xml.XmlException)
        {
            return Result.Fail<ImportedIcon>(
                new CustomIconImportError(CustomIconImportFailureReason.MalformedXml));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Result.Fail<ImportedIcon>(
                new CustomIconImportError(CustomIconImportFailureReason.Unreadable));
        }
    }
}
