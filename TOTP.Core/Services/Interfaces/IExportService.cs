using FluentResults;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IExportService
{
    Task<Result> ExportToEncryptedStreamAsync(
        IEnumerable<Account> accounts,
        string password,
        Stream destination,
        ExportFileFormat format,
        CancellationToken cancellationToken = default);

    Task<Result> ExportToStreamAsync(
        IEnumerable<Account> accounts,
        Stream destination,
        ExportFileFormat format,
        CancellationToken cancellationToken = default);

    Task<Result<List<Account>>> ImportFromEncryptedStreamAsync(
        string password,
        Stream source,
        CancellationToken cancellationToken = default);

    Task<Result<List<Account>>> ImportFromStreamAsync(
        Stream source,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verschlüsselt die OtpEntries und speichert sie sicher in einer Datei.
    /// </summary>
    Task<Result> ExportToEncryptedFileAsync(IEnumerable<Account> accounts, string password, string filePath, ExportFileFormat format);

    /// <summary>
    /// Exportiert die OtpEntries unverschlüsselt im gewünschten Dateiformat.
    /// </summary>
    Task<Result> ExportToFileAsync(IEnumerable<Account> accounts, string filePath, ExportFileFormat format);

    /// <summary>
    /// Liest eine verschlüsselte Datei ein, validiert sie und gibt die Accounts zurück.
    /// </summary>
    Task<Result<List<Account>>> ImportFromEncryptedFileAsync(string password, string filePath);

    /// <summary>
    /// Importiert OTP-Accounts aus einer Datei.
    /// Unterstützt verschlüsselte .totp sowie unverschlüsselte .json/.txt/.csv,
    /// einschließlich unverschlüsselter Aegis-JSON-Vaults und 2FAS-.2fas-Dateien.
    /// </summary>
    Task<Result<List<Account>>> ImportFromFileAsync(string filePath, string? password = null);
}
