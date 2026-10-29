using TOTP.Camera.OpenCv;
using TOTP.Core.Platform;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaPlatformCapabilityReport(
    IPlatformQuickUnlock quickUnlock,
    IEnumerable<IPlatformSecretStore> secretStores,
    IPlatformSessionEventSource sessionEvents,
    IAsyncPlatformClipboard clipboard,
    IEnumerable<ICameraAccessProbe> cameraProbes,
    IUpdateInstallerLauncher installer,
    IAvaloniaLocalizationService localization) : IPlatformCapabilityReport
{
    public async Task<IReadOnlyList<PlatformCapability>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var capabilities = new List<PlatformCapability>
        {
            await CaptureQuickUnlockAsync(cancellationToken),
            new(localization.GetString(AvaloniaStringKeys.CapabilitySessionLock), sessionEvents is IPlatformCapabilityStatusProvider sessionCapability
                ? sessionCapability.CapabilityStatus
                : sessionEvents.IsSupported
                    ? PlatformCapabilityStatus.Supported
                    : PlatformCapabilityStatus.PermanentlyUnavailable),
            new(localization.GetString(AvaloniaStringKeys.CapabilityClipboardWrite), HasClipboard(ClipboardCapabilities.WriteText)
                ? PlatformCapabilityStatus.Supported
                : PlatformCapabilityStatus.TemporarilyUnavailable),
            new(localization.GetString(AvaloniaStringKeys.CapabilityConditionalClipboardClear), HasClipboard(ClipboardCapabilities.ConditionalClear)
                ? PlatformCapabilityStatus.Supported
                : PlatformCapabilityStatus.PermanentlyUnavailable),
            CaptureCamera(),
            new(localization.GetString(AvaloniaStringKeys.CapabilityUpdateInstallation), installer.IsSupported
                ? PlatformCapabilityStatus.Supported
                : PlatformCapabilityStatus.PermanentlyUnavailable)
        };

        foreach (var store in secretStores)
            capabilities.Add(await CaptureSecretStoreAsync(store, cancellationToken));
        return capabilities;
    }

    private async Task<PlatformCapability> CaptureQuickUnlockAsync(CancellationToken cancellationToken)
    {
        try
        {
            var availability = await quickUnlock.GetAvailabilityAsync(cancellationToken);
            return new PlatformCapability(localization.GetString(AvaloniaStringKeys.CapabilityQuickUnlock), availability switch
            {
                PlatformQuickUnlockAvailability.Available => PlatformCapabilityStatus.Supported,
                PlatformQuickUnlockAvailability.NotSupported => PlatformCapabilityStatus.PermanentlyUnavailable,
                PlatformQuickUnlockAvailability.NotConfigured => PlatformCapabilityStatus.Misconfigured,
                PlatformQuickUnlockAvailability.DisabledByPolicy => PlatformCapabilityStatus.PermissionDenied,
                PlatformQuickUnlockAvailability.TemporarilyUnavailable => PlatformCapabilityStatus.TemporarilyUnavailable,
                _ => PlatformCapabilityStatus.Failed
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new PlatformCapability(
                localization.GetString(AvaloniaStringKeys.CapabilityQuickUnlock),
                PlatformCapabilityStatus.Failed);
        }
    }

    private async Task<PlatformCapability> CaptureSecretStoreAsync(
        IPlatformSecretStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await store.GetAvailabilityAsync(cancellationToken);
            return new PlatformCapability(localization.GetString(AvaloniaStringKeys.CapabilityDeviceSecretStore), availability switch
            {
                PlatformSecretStoreAvailability.Available => PlatformCapabilityStatus.Supported,
                PlatformSecretStoreAvailability.NotSupported => PlatformCapabilityStatus.PermanentlyUnavailable,
                PlatformSecretStoreAvailability.NotConfigured => PlatformCapabilityStatus.Misconfigured,
                PlatformSecretStoreAvailability.DisabledByPolicy => PlatformCapabilityStatus.PermissionDenied,
                PlatformSecretStoreAvailability.TemporarilyUnavailable => PlatformCapabilityStatus.TemporarilyUnavailable,
                _ => PlatformCapabilityStatus.Failed
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new PlatformCapability(
                localization.GetString(AvaloniaStringKeys.CapabilityDeviceSecretStore),
                PlatformCapabilityStatus.Failed);
        }
    }

    private PlatformCapability CaptureCamera()
    {
        var probe = cameraProbes.FirstOrDefault();
        if (probe is null)
            return new PlatformCapability(
                localization.GetString(AvaloniaStringKeys.CapabilityCamera),
                PlatformCapabilityStatus.Supported);
        try
        {
            return new PlatformCapability(localization.GetString(AvaloniaStringKeys.CapabilityCamera), probe.Probe() switch
            {
                CameraAccessStatus.Ready => PlatformCapabilityStatus.Supported,
                CameraAccessStatus.PermissionDenied => PlatformCapabilityStatus.PermissionDenied,
                CameraAccessStatus.NoCamera => PlatformCapabilityStatus.TemporarilyUnavailable,
                _ => PlatformCapabilityStatus.TemporarilyUnavailable
            });
        }
        catch
        {
            return new PlatformCapability(
                localization.GetString(AvaloniaStringKeys.CapabilityCamera),
                PlatformCapabilityStatus.Failed);
        }
    }

    private bool HasClipboard(ClipboardCapabilities capability) =>
        (clipboard.Capabilities & capability) == capability;
}
