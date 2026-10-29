using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Avalonia.Desktop.Startup;

public sealed class AvaloniaStartupCoordinator(
    ISettingsService settingsService,
    IAuthorizationService authorizationService,
    IAvaloniaLocalizationService localizationService,
    ILogger<AvaloniaStartupCoordinator> logger,
    IStartupDiagnostics? startupDiagnostics = null) : IAvaloniaStartupCoordinator
{
    public async Task<AvaloniaStartupOutcome> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stageStarted = Stopwatch.GetTimestamp();
            var settings = await settingsService.LoadAsync();
            startupDiagnostics?.Record(
                StartupDiagnosticStage.Preferences,
                Stopwatch.GetElapsedTime(stageStarted),
                settings.IsSuccess);
            if (settings.IsFailed)
            {
                logger.LogError("Avalonia startup could not load application preferences.");
                RecordCompleted(false);
                return AvaloniaStartupOutcome.PreferencesUnavailable;
            }

            localizationService.ApplyCulture(settings.Value.CultureName);

            cancellationToken.ThrowIfCancellationRequested();
            stageStarted = Stopwatch.GetTimestamp();
            await authorizationService.InitializeAsync();
            startupDiagnostics?.Record(
                StartupDiagnosticStage.Authorization,
                Stopwatch.GetElapsedTime(stageStarted),
                succeeded: true);
            cancellationToken.ThrowIfCancellationRequested();

            if (!authorizationService.State.IsConfigured)
            {
                RecordCompleted(true);
                return AvaloniaStartupOutcome.ReadyForPasswordSetup;
            }

            if (authorizationService.State.PreferredUnlockMethod != PreferredUnlockMethod.PlatformQuickUnlock)
            {
                RecordCompleted(true);
                return AvaloniaStartupOutcome.ReadyForUnlock;
            }

            stageStarted = Stopwatch.GetTimestamp();
            var quickUnlock = await authorizationService.TryUnlockOnStartupAsync(cancellationToken);
            startupDiagnostics?.Record(
                StartupDiagnosticStage.QuickUnlock,
                Stopwatch.GetElapsedTime(stageStarted),
                quickUnlock == AuthorizationResult.Success);
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = quickUnlock == AuthorizationResult.Success
                && authorizationService.State.IsUnlocked
                    ? AvaloniaStartupOutcome.ReadyUnlocked
                    : AvaloniaStartupOutcome.ReadyForPasswordFallback;
            RecordCompleted(true);
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not log exception messages here: boundary exceptions may contain
            // paths or user-controlled data. The type and stage are sufficient.
            logger.LogError(
                "Avalonia startup failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            RecordCompleted(false);
            return AvaloniaStartupOutcome.UnexpectedFailure;
        }

        void RecordCompleted(bool succeeded) => startupDiagnostics?.Record(
            StartupDiagnosticStage.Completed,
            Stopwatch.GetElapsedTime(totalStarted),
            succeeded);
    }
}
