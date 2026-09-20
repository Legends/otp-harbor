using System.Text;
using System.Text.Json;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class AppearanceSettingsService : IAppearanceSettingsService
{
    private const int FormatVersion = 1;
    private const string FileName = "appearance-settings.json";

    private readonly string _settingsPath;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly ILogger<AppearanceSettingsService> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private int _themePreference = (int)AppThemePreference.Dark;

    public AppearanceSettingsService(
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity,
        ILogger<AppearanceSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsPath = Path.Combine(
            applicationPaths.ApplicationDataDirectory,
            FileName);
        TryLoad();
    }

    public event EventHandler? PreferenceChanged;

    public AppThemePreference ThemePreference =>
        (AppThemePreference)Volatile.Read(ref _themePreference);

    public async Task<Result> SetThemePreferenceAsync(
        AppThemePreference preference,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(preference))
            return Result.Fail("The requested appearance theme is invalid.");
        if (ThemePreference == preference) return Result.Ok();

        await _saveLock.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            if (ThemePreference == preference) return Result.Ok();
            var directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("The appearance settings directory is unavailable.");
            Directory.CreateDirectory(directory);
            _fileSecurity.RestrictDirectoryToCurrentUser(directory);
            temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
            var json = $"{{\"version\":{FormatVersion},\"theme\":\"{preference}\"}}";
            await File.WriteAllBytesAsync(
                temporaryPath,
                Encoding.UTF8.GetBytes(json),
                cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryPath);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            temporaryPath = null;
            _fileSecurity.RestrictFileToCurrentUser(_settingsPath);
            Volatile.Write(ref _themePreference, (int)preference);
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Appearance settings could not be saved.");
            return Result.Fail("Appearance settings could not be saved.");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug("A temporary appearance settings file could not be removed.");
                }
            }
            _saveLock.Release();
        }
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (stream.Length is <= 0 or > 4096) return;
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                MaxDepth = 4,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var parsedVersion)
                || parsedVersion != FormatVersion
                || !root.TryGetProperty("theme", out var theme)
                || theme.ValueKind != JsonValueKind.String
                || !Enum.TryParse<AppThemePreference>(theme.GetString(), out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return;
            }

            Volatile.Write(ref _themePreference, (int)parsed);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Appearance settings could not be loaded; the dark theme will be used.");
        }
    }
}
