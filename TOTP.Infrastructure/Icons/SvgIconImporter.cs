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
                "custom-svg"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
        {
            return Result.Fail("The selected custom SVG is invalid or unsupported.");
        }
    }
}
