using System.Text.Json;
using FluentResults;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Parser;

internal sealed class AccountImportDispatcher : IDisposable
{
    private sealed record JsonProvider(AccountImportFormat Format, Func<JsonElement, bool> Recognizes,
        Func<JsonElement, Result<List<Account>>> Parse);

    private static readonly JsonProvider[] JsonProviders =
    [
        new(AccountImportFormat.AegisJson, root => root.TryGetProperty("db", out _) && root.TryGetProperty("version", out _), root => Result.Ok(AegisVaultParser.Parse(root))),
        new(AccountImportFormat.TwoFasJson, root => root.TryGetProperty("services", out _) || root.TryGetProperty("schemaVersion", out _), root => Result.Ok(TwoFasBackupParser.Parse(root))),
        new(AccountImportFormat.BitwardenJson, root => root.TryGetProperty("items", out _) && root.TryGetProperty("encrypted", out _), AuthenticatorExportParser.ParseBitwarden),
        new(AccountImportFormat.ProtonJson, root => root.TryGetProperty("entries", out _) && root.TryGetProperty("version", out _), AuthenticatorExportParser.ParseProton)
    ];

    private readonly string _content;
    private readonly Func<string, ExportFileFormat, List<Account>> _native;
    private readonly Func<string, List<List<string>>> _csv;
    private readonly Func<List<Account>, List<Account>> _validate;
    private JsonDocument? _json;
    private List<List<string>>? _rows;
    private string[] _lines = [];

    public AccountImportDispatcher(string content, Func<string, ExportFileFormat, List<Account>> native,
        Func<string, List<List<string>>> csv, Func<List<Account>, List<Account>> validate)
    { _content = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n'); _native = native; _csv = csv; _validate = validate; }

    public IReadOnlyList<AccountImportFormat> Detect()
    {
        if (_content.StartsWith('{') || _content.StartsWith('['))
        {
            _json = JsonDocument.Parse(_content);
            var root = _json.RootElement;
            if (root.ValueKind == JsonValueKind.Array) return [AccountImportFormat.OtpHarborJson];
            if (root.ValueKind != JsonValueKind.Object) return [];
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                throw new FormatException("Duplicate export schema fields.");
            if (root.TryGetProperty("encrypted", out var encrypted) && encrypted.ValueKind == JsonValueKind.True
                || root.TryGetProperty("encryptedData", out _) || root.TryGetProperty("salt", out _) || root.TryGetProperty("kdfParams", out _)
                || root.TryGetProperty("db", out var db) && db.ValueKind == JsonValueKind.String
                || root.TryGetProperty("servicesEncrypted", out var services) && services.ValueKind is not JsonValueKind.Null)
                return [AccountImportFormat.EncryptedExternalExport];
            return JsonProviders.Where(parser => parser.Recognizes(root)).Select(parser => parser.Format).ToArray();
        }
        _lines = _content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
        if (_lines.Length > 0 && _lines[0].StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) return [AccountImportFormat.TotpUriList];
        if (_lines.Length == 0) return [];
        if (_lines[0].Contains('|')) return [AccountImportFormat.OtpHarborText];
        if (!_lines[0].Contains(',')) return [];
        _rows = _csv(_content);
        if (_rows.Count == 0) return [];
        var header = _rows[0];
        var formats = new List<AccountImportFormat>();
        if (header.Contains("login_totp", StringComparer.OrdinalIgnoreCase)
            && header.Contains("name", StringComparer.OrdinalIgnoreCase) && header.Contains("type", StringComparer.OrdinalIgnoreCase)) formats.Add(AccountImportFormat.BitwardenCsv);
        if (header.Take(4).SequenceEqual(new[] { "id", "issuer", "account_name", "secret" }, StringComparer.OrdinalIgnoreCase)) formats.Add(AccountImportFormat.OtpHarborCsv);
        return formats;
    }

    public Result<List<Account>> Parse(AccountImportFormat format)
    {
        if (format == AccountImportFormat.EncryptedExternalExport)
            return Result.Fail<List<Account>>(new AccountImportReadError(new(0, unsupportedCount: 1) { Format = format }))
                .WithError(new AppError(AppErrorCode.ImportInvalidPayload, "Encrypted external exports are unsupported."));
        var provider = JsonProviders.FirstOrDefault(parser => parser.Format == format);
        var result = provider is not null ? provider.Parse(_json!.RootElement) : format switch
        {
            AccountImportFormat.OtpHarborJson => Result.Ok(_native(_content, ExportFileFormat.Json)),
            AccountImportFormat.OtpHarborCsv => Result.Ok(_native(_content, ExportFileFormat.Csv)),
            AccountImportFormat.OtpHarborText => Result.Ok(_native(_content, ExportFileFormat.Txt)),
            AccountImportFormat.BitwardenCsv => AuthenticatorExportParser.ParseBitwardenCsv(_rows!),
            AccountImportFormat.TotpUriList => AuthenticatorExportParser.ParseUris(_lines),
            _ => Result.Fail<List<Account>>(new AppError(AppErrorCode.ImportInvalidPayload, "Unsupported selected export format."))
        };
        AccountImportReadSummary Describe(AccountImportReadSummary value) => new(value.SupportedCount,
            value.InvalidCount, value.UnsupportedCount, value.NonOtpCount) { Format = format };
        if (result.IsFailed)
            return Result.Fail<List<Account>>(result.Errors.Select(error => error is AccountImportReadError read
                ? new AccountImportReadError(Describe(read.Summary)) : error));
        return Result.Ok(_validate(result.Value)).WithSuccess(Describe(result.Successes.OfType<AccountImportReadSummary>().FirstOrDefault()
            ?? new AccountImportReadSummary(result.Value.Count)));
    }

    public void Dispose() => _json?.Dispose();
}
