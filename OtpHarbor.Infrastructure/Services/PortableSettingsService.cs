using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

/// <summary>
/// Persists non-secret application preferences through the portable preferences
/// contract. Authorization data is deliberately excluded by the mapper.
/// </summary>
public sealed class PortableSettingsService : ISettingsService, IDisposable
{
    private readonly IAppPreferencesStore _store;
    private readonly ILogger<PortableSettingsService> _logger;
    private readonly IBrandIconPackService? _brandIconPackService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly AppSettings _current = new();
    private bool _isLoaded;

    public PortableSettingsService(
        IAppPreferencesStore store,
        ILogger<PortableSettingsService> logger,
        IBrandIconPackService? brandIconPackService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _brandIconPackService = brandIconPackService;
    }

    public IAppSettings Current => _current;

    public async Task<Result<IAppSettings>> LoadAsync()
    {
        if (_isLoaded) return Result.Ok<IAppSettings>(_current);

        await _lock.WaitAsync();
        try
        {
            if (_isLoaded) return Result.Ok<IAppSettings>(_current);

            var loaded = await _store.LoadAsync();
            if (loaded.IsFailed)
            {
                _logger.LogError("Loading portable application preferences failed.");
                return Result.Fail<IAppSettings>(loaded.Errors);
            }

            if (loaded.Value is not null)
            {
                await MigrateLegacyBrandDisplayPreferenceAsync(loaded.Value);
                AppPreferencesMapper.ApplyTo(loaded.Value, _current);
            }

            _isLoaded = true;
            return Result.Ok<IAppSettings>(_current);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while loading portable application preferences.");
            return Result.Fail<IAppSettings>(new AppError(
                AppErrorCode.SettingsServiceLoadFailed,
                "Settings service failed while loading preferences.",
                ex));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result> SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var preferences = AppPreferencesMapper.FromSettings(_current);
            var saved = await _store.SaveAsync(preferences);
            if (saved.IsFailed)
            {
                _logger.LogError("Persisting portable application preferences failed.");
                return saved;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist portable application preferences.");
            return Result.Fail(new AppError(
                AppErrorCode.SettingsServiceSaveFailed,
                "Settings service failed while saving preferences.",
                ex));
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task MigrateLegacyBrandDisplayPreferenceAsync(AppPreferencesV1 preferences)
    {
        if (preferences.LegacyShowIssuerLogo is not { } showIssuerLogo
            || _brandIconPackService is null)
        {
            return;
        }

        var migrated = await _brandIconPackService.SetShowIssuerLogoAsync(showIssuerLogo);
        if (migrated.IsFailed)
        {
            _logger.LogWarning("The legacy brand-icon display preference could not be migrated.");
            return;
        }

        var sanitized = preferences with { LegacyShowIssuerLogo = null };
        var saved = await _store.SaveAsync(sanitized);
        if (saved.IsFailed)
            _logger.LogWarning("The migrated application preferences could not be sanitized.");
    }
}
