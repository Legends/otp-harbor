using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using FluentResults;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Validation;
using TOTP.Infrastructure.Common;
using TOTP.Infrastructure.Parser;

namespace TOTP.Infrastructure.Services;

public sealed class ExportService : IExportService
{
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("TOTP");
    private const int SaltSize = 16;
    private const long MaxImportFileBytes = 5 * 1024 * 1024; // 5 MiB hard limit to reduce parser/DoS risk.

    private static readonly Argon2Parameters _argonParameters = new()
    {
        DegreeOfParallelism = 1,
        MemorySize = 128 * 1024,
        NumberOfPasses = 4
    };

    private static readonly Argon2id _kdf = PasswordBasedKeyDerivationAlgorithm.Argon2id(in _argonParameters);
    private static readonly AeadAlgorithm _algoAead = AeadAlgorithm.Aes256Gcm;
    private readonly ILogger<ExportService> _logger;

    public ExportService(ILogger<ExportService> logger)
    {
        _logger = logger;
    }

    public async Task<Result<List<Account>>> ImportFromFileAsync(string filePath, string? password = null)
    {
        try
        {
            var sizeValidation = ValidateImportFileSize(filePath);
            if (sizeValidation.IsFailed)
            {
                return Result.Fail(sizeValidation.Errors);
            }

            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await ImportFromStreamAsync(stream, Path.GetFileName(filePath), password);
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("path import", ex);
            return Result.Fail(ExportServiceErrorMapper.MapImportError(ex));
        }
    }

    public async Task<Result> ExportToFileAsync(IEnumerable<Account> accounts, string filePath, ExportFileFormat format)
    {
        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            return await ExportToStreamAsync(accounts, stream, format);
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("path export", ex);
            return Result.Fail(ExportServiceErrorMapper.MapExportError(ex));
        }
    }

    public async Task<Result> ExportToEncryptedFileAsync(IEnumerable<Account> accounts, string password, string filePath, ExportFileFormat format)
    {
        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            return await ExportToEncryptedStreamAsync(accounts, password, stream, format);
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("encrypted path export", ex);
            return Result.Fail(ExportServiceErrorMapper.MapExportError(ex));
        }
    }

    public async Task<Result<List<Account>>> ImportFromEncryptedFileAsync(string password, string filePath)
    {
        try
        {
            var sizeValidation = ValidateImportFileSize(filePath);
            if (sizeValidation.IsFailed)
            {
                return Result.Fail(sizeValidation.Errors);
            }

            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await ImportFromEncryptedStreamAsync(password, stream);
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("encrypted path import", ex);
            return Result.Fail(ExportServiceErrorMapper.MapImportError(ex));
        }
    }

    public async Task<Result> ExportToStreamAsync(
        IEnumerable<Account> accounts,
        Stream destination,
        ExportFileFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(destination);
        byte[]? payloadBytes = null;

        try
        {
            payloadBytes = Encoding.UTF8.GetBytes(SerializeTokens(accounts, format));
            await destination.WriteAsync(payloadBytes, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("stream export", ex);
            return Result.Fail(ExportServiceErrorMapper.MapExportError(ex));
        }
        finally
        {
            Clear(payloadBytes);
        }
    }

    public async Task<Result> ExportToEncryptedStreamAsync(
        IEnumerable<Account> accounts,
        string password,
        Stream destination,
        ExportFileFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(destination);
        byte[]? plaintext = null;
        byte[]? passwordBytes = null;
        byte[]? salt = null;
        byte[]? nonce = null;
        byte[]? ciphertextWithTag = null;

        try
        {
            plaintext = Encoding.UTF8.GetBytes(SerializeTokens(accounts, format));
            passwordBytes = Encoding.UTF8.GetBytes(password);
            salt = RandomNumberGenerator.GetBytes(SaltSize);
            nonce = RandomNumberGenerator.GetBytes(_algoAead.NonceSize);

            using var key = _kdf.DeriveKey(passwordBytes, salt, _algoAead);
            ciphertextWithTag = _algoAead.Encrypt(key, nonce, default, plaintext);

            await destination.WriteAsync(MagicBytes, cancellationToken);
            await destination.WriteAsync(new byte[] { (byte)format }, cancellationToken);
            await destination.WriteAsync(salt, cancellationToken);
            await destination.WriteAsync(nonce, cancellationToken);
            await destination.WriteAsync(ciphertextWithTag, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("encrypted stream export", ex);
            return Result.Fail(ExportServiceErrorMapper.MapExportError(ex));
        }
        finally
        {
            Clear(plaintext);
            Clear(passwordBytes);
            Clear(salt);
            Clear(nonce);
            Clear(ciphertextWithTag);
        }
    }

    public async Task<Result<List<Account>>> ImportFromStreamAsync(
        Stream source,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var extension = Path.GetExtension(Path.GetFileName(fileName));

        if (extension.Equals(".totp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return Result.Fail(new AppError(
                    AppErrorCode.ImportWrongPasswordOrTampered,
                    "Password is required for encrypted import."));
            }

            return await ImportFromEncryptedStreamAsync(password, source, cancellationToken);
        }

        if (!TryGetUnencryptedFormat(extension, out var format))
        {
            return Result.Fail(new AppError(AppErrorCode.ImportInvalidFile, "Unsupported import file extension."));
        }

        byte[]? fileBytes = null;
        try
        {
            (fileBytes, var length) = await ReadBoundedAsync(source, cancellationToken);
            var content = Encoding.UTF8.GetString(fileBytes, 0, length);
            return Result.Ok(DeserializeTokens(content, format));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ImportSizeLimitExceededException)
        {
            return ImportSizeFailure<List<Account>>();
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("stream import", ex);
            return Result.Fail(ExportServiceErrorMapper.MapImportError(ex));
        }
        finally
        {
            Clear(fileBytes);
        }
    }

    public async Task<Result<List<Account>>> ImportFromEncryptedStreamAsync(
        string password,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(source);
        byte[]? passwordBytes = null;
        byte[]? decryptedBytes = null;
        byte[]? fileBytes = null;

        try
        {
            (fileBytes, var fileLength) = await ReadBoundedAsync(source, cancellationToken);
            int nonceSize = _algoAead.NonceSize;
            int legacyHeaderSize = MagicBytes.Length + SaltSize + nonceSize;
            int versionedHeaderSize = MagicBytes.Length + 1 + SaltSize + nonceSize;

            if (fileLength < legacyHeaderSize + _algoAead.TagSize)
            {
                return Result.Fail(new AppError(AppErrorCode.ImportInvalidFile, "Encrypted import file is invalid."));
            }

            if (!fileBytes.AsSpan(0, MagicBytes.Length).SequenceEqual(MagicBytes))
            {
                return Result.Fail(new AppError(AppErrorCode.ImportInvalidFile, "Encrypted import file header is invalid."));
            }

            ExportFileFormat format = ExportFileFormat.Json;
            int offset = MagicBytes.Length;

            if (fileLength >= versionedHeaderSize + _algoAead.TagSize)
            {
                var marker = fileBytes[offset];
                if (marker is (byte)ExportFileFormat.Json or (byte)ExportFileFormat.Txt or (byte)ExportFileFormat.Csv)
                {
                    format = (ExportFileFormat)marker;
                    offset += 1;
                }
            }

            var salt = fileBytes.AsSpan(offset, SaltSize);
            var nonce = fileBytes.AsSpan(offset + SaltSize, nonceSize);
            var encryptedData = fileBytes.AsSpan(offset + SaltSize + nonceSize, fileLength - offset - SaltSize - nonceSize);

            passwordBytes = Encoding.UTF8.GetBytes(password);
            using var key = _kdf.DeriveKey(passwordBytes, salt, _algoAead);

            decryptedBytes = new byte[encryptedData.Length - _algoAead.TagSize];
            if (!_algoAead.Decrypt(key, nonce, default, encryptedData, decryptedBytes))
            {
                return Result.Fail(new AppError(AppErrorCode.ImportWrongPasswordOrTampered, "Import decryption failed."));
            }

            var content = Encoding.UTF8.GetString(decryptedBytes);
            return Result.Ok(DeserializeTokens(content, format));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ImportSizeLimitExceededException)
        {
            return ImportSizeFailure<List<Account>>();
        }
        catch (Exception ex)
        {
            LogBoundaryFailure("encrypted stream import", ex);
            return Result.Fail(ExportServiceErrorMapper.MapImportError(ex));
        }
        finally
        {
            Clear(passwordBytes);
            Clear(decryptedBytes);
            Clear(fileBytes);
        }
    }

    private async Task<(byte[] Buffer, int Length)> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxImportFileBytes + 1];
        var totalRead = 0;
        try
        {
            while (totalRead < buffer.Length)
            {
                var read = await source.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
                if (read == 0)
                {
                    return (buffer, totalRead);
                }

                totalRead += read;
            }

            throw new ImportSizeLimitExceededException();
        }
        catch
        {
            Clear(buffer);
            throw;
        }
    }

    private static Result<T> ImportSizeFailure<T>() => Result.Fail<T>(new AppError(
        AppErrorCode.ImportInvalidFile,
        $"Import file exceeds maximum allowed size ({MaxImportFileBytes / (1024 * 1024)} MiB)."));

    private void LogBoundaryFailure(string operation, Exception exception) =>
        _logger.LogError(
            "Export service {Operation} failed with exception type {ExceptionType}.",
            operation,
            exception.GetType().Name);

    private static void Clear(byte[]? buffer)
    {
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private sealed class ImportSizeLimitExceededException : Exception;

    private static string SerializeTokens(IEnumerable<Account> accounts, ExportFileFormat format)
    {
        return format switch
        {
            ExportFileFormat.Json => JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }),
            ExportFileFormat.Txt => BuildTxt(accounts),
            ExportFileFormat.Csv => BuildCsv(accounts),
            _ => JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true })
        };
    }

    private static List<Account> DeserializeTokens(string content, ExportFileFormat format)
    {
        return format switch
        {
            ExportFileFormat.Json => ParseJson(content),
            ExportFileFormat.Txt => ParseTxt(content),
            ExportFileFormat.Csv => ParseCsv(content),
            _ => JsonSerializer.Deserialize<List<Account>>(content) ?? []
        };
    }

    private static List<Account> ParseJson(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        return root.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<Account>>(content) ?? [],
            JsonValueKind.Object when root.TryGetProperty("db", out _) => AegisVaultParser.Parse(root),
            JsonValueKind.Object when root.TryGetProperty("services", out _)
                || root.TryGetProperty("schemaVersion", out _) => TwoFasBackupParser.Parse(root),
            _ => throw new FormatException("The JSON import payload is unsupported.")
        };
    }

    private static string BuildCsv(IEnumerable<Account> accounts)
    {
        static string Escape(string? value)
        {
            var v = value ?? string.Empty;
            if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            {
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }

            return v;
        }

        var lines = new List<string> { "id,issuer,account_name,secret,period_seconds" };
        lines.AddRange(accounts.Select(a =>
            $"{a.ID},{Escape(a.Issuer)},{Escape(a.AccountName)},{Escape(a.Secret)},{a.PeriodSeconds}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTxt(IEnumerable<Account> accounts)
    {
        var lines = new List<string> { "issuer|account_name|secret|id|period_seconds" };
        lines.AddRange(accounts.Select(a =>
            $"{a.Issuer}|{a.AccountName}|{a.Secret}|{a.ID}|{a.PeriodSeconds}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static List<Account> ParseTxt(string content)
    {
        var result = new List<Account>();
        var lines = content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)
                && !line.TrimStart().StartsWith('#'))
            .ToArray();
        if (lines.Length > 0
            && lines[0].TrimStart().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseOtpAuthLines(lines.Select(line => line.Trim()));
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("issuer|account_name|secret|id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 3)
            {
                continue;
            }

            var issuer = parts[0];
            var accountName = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1];
            var secret = parts[2];
            var id = parts.Length >= 4 && Guid.TryParse(parts[3], out var parsedId) ? parsedId : Guid.NewGuid();
            var periodSeconds = ParsePeriod(parts.Length >= 5 ? parts[4] : null);
            result.Add(new Account(id, issuer, secret, accountName, periodSeconds));
        }

        return result;
    }

    private static List<Account> ParseOtpAuthLines(IEnumerable<string> lines)
    {
        var result = new List<Account>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("The text import mixes incompatible account formats.");

            var parsed = OtpauthParser.Parse(line);
            if (!OtpAuthSupportPolicy.IsSupported(parsed))
                throw new FormatException("The text import contains unsupported TOTP parameters.");

            result.Add(new Account(
                Guid.NewGuid(),
                parsed.Issuer?.Trim() ?? string.Empty,
                parsed.SecretBase32,
                EmptyToNull(parsed.Label.Trim()),
                parsed.Period));
        }

        return result;
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static List<Account> ParseCsv(string content)
    {
        var result = new List<Account>();
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            return result;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var row = SplitCsvLine(lines[i]);
            if (row.Count < 4)
            {
                continue;
            }

            var id = Guid.TryParse(row[0], out var parsedId) ? parsedId : Guid.NewGuid();
            var issuer = row[1];
            var accountName = string.IsNullOrWhiteSpace(row[2]) ? null : row[2];
            var secret = row[3];
            var periodSeconds = ParsePeriod(row.Count >= 5 ? row[4] : null);
            result.Add(new Account(id, issuer, secret, accountName, periodSeconds));
        }

        return result;
    }

    private static int ParsePeriod(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? TotpPeriodPolicy.DefaultSeconds
            : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var periodSeconds)
                ? periodSeconds
                : 0;

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString());
        return values;
    }

    private static bool TryGetUnencryptedFormat(string extension, out ExportFileFormat format)
    {
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            format = ExportFileFormat.Json;
            return true;
        }

        if (extension.Equals(".2fas", StringComparison.OrdinalIgnoreCase))
        {
            format = ExportFileFormat.Json;
            return true;
        }

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            format = ExportFileFormat.Txt;
            return true;
        }

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            format = ExportFileFormat.Csv;
            return true;
        }

        format = ExportFileFormat.Json;
        return false;
    }

    private static Result ValidateImportFileSize(string filePath)
    {
        var info = new FileInfo(filePath);
        if (info.Exists && info.Length > MaxImportFileBytes)
        {
            return Result.Fail(new AppError(
                AppErrorCode.ImportInvalidFile,
                $"Import file exceeds maximum allowed size ({MaxImportFileBytes / (1024 * 1024)} MiB)."));
        }

        return Result.Ok();
    }
}
