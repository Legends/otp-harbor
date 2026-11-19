using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _initialQuickUnlockLifetime;

    private async Task OfferInitialQuickUnlockAsync()
    {
        if (_disposed || _shutdownPrepared || !IsShellVisible) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _initialQuickUnlockLifetime = operation;
        var capabilityConsumed = false;
        try
        {
            PreferredUnlockMethod? selected = null;
            if (_dialogs is not null)
            {
                var quickUnlock = QueryInitialUnlockAvailabilityAsync(PreferredUnlockMethod.PlatformQuickUnlock);
                var deviceCredential = QueryInitialUnlockAvailabilityAsync(PreferredUnlockMethod.PlatformDeviceCredential);
                await Task.WhenAll(quickUnlock, deviceCredential);
                if (!CanCompleteInitialSetup(operation.Token)) return;
                var title = _localization.GetString(AvaloniaStringKeys.InitialQuickUnlockTitle);
                var message = _localization.GetString(AvaloniaStringKeys.InitialQuickUnlockDescription);
                var skip = _localization.GetString(AvaloniaStringKeys.NotNow);
                if (quickUnlock.Result && deviceCredential.Result)
                {
                    var choice = await _dialogs.ChooseAsync(new ChoiceDialogRequest(title, message,
                        NotificationSeverity.Information,
                        _localization.GetString(AvaloniaStringKeys.EnableQuickUnlock),
                        _localization.GetString(AvaloniaStringKeys.DeviceScreenLock), skip, StackActions: true), operation.Token);
                    selected = choice switch
                    {
                        ChoiceDialogResult.Primary => PreferredUnlockMethod.PlatformQuickUnlock,
                        ChoiceDialogResult.Secondary => PreferredUnlockMethod.PlatformDeviceCredential,
                        _ => null
                    };
                }
                else if (quickUnlock.Result || deviceCredential.Result)
                {
                    var method = quickUnlock.Result ? PreferredUnlockMethod.PlatformQuickUnlock
                        : PreferredUnlockMethod.PlatformDeviceCredential;
                    var label = quickUnlock.Result ? AvaloniaStringKeys.EnableQuickUnlock : AvaloniaStringKeys.DeviceScreenLock;
                    if (await _dialogs.ConfirmAsync(new ConfirmationDialogRequest(title, message,
                            NotificationSeverity.Information, _localization.GetString(label), skip, StackActions: true), operation.Token))
                        selected = method;
                }
            }

            if (!CanCompleteInitialSetup(operation.Token)) return;
            capabilityConsumed = true;
            var result = await _authorizationService.CompleteInitialQuickUnlockSetupAsync(selected, operation.Token);
            if (!CanCompleteInitialSetup(operation.Token) || selected is null) return;
            if (result == AuthorizationResult.Success)
            {
                StatusText = _localization.GetString(AvaloniaStringKeys.QuickUnlockEnabled);
                StatusSeverity = NotificationSeverity.Success;
            }
            else if (result != AuthorizationResult.Cancelled)
                ShowInitialQuickUnlockFailure();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (CanCompleteInitialSetup(operation.Token)) ShowInitialQuickUnlockFailure();
        }
        finally
        {
            if (!capabilityConsumed && CanCompleteInitialSetup(operation.Token))
            {
                try { await _authorizationService.CompleteInitialQuickUnlockSetupAsync(null, operation.Token); }
                catch (Exception) { ShowInitialQuickUnlockFailure(); }
            }
            _initialQuickUnlockLifetime = null;
        }
    }

    private bool CanCompleteInitialSetup(CancellationToken token) =>
        !token.IsCancellationRequested && !_disposed && !_shutdownPrepared
        && IsShellVisible && _authorizationService.State is { IsUnlocked: true };

    private async Task<bool> QueryInitialUnlockAvailabilityAsync(PreferredUnlockMethod method)
    {
        try
        {
            return method == PreferredUnlockMethod.PlatformQuickUnlock
                ? await _authorizationService.IsHelloAvailableAsync()
                : await _authorizationService.IsUnlockMethodAvailableAsync(method);
        }
        catch (Exception) { return false; }
    }

    private void ShowInitialQuickUnlockFailure()
    {
        StatusText = _localization.GetString(AvaloniaStringKeys.QuickUnlockEnrollmentFailed);
        StatusSeverity = NotificationSeverity.Warning;
    }
}
