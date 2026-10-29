using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using System.Runtime.Versioning;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Core.Security.Models;
using TOTP.Platform.Android;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidBiometricPrompt : IAndroidBiometricPrompt
{
    private const int SecurityUpdateRequiredErrorCode = 15;

    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly MobileStringCatalog _strings;

    public AndroidBiometricPrompt(
        Context context,
        AndroidActivityProvider activityProvider,
        MobileStringCatalog strings)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _activityProvider = activityProvider
            ?? throw new ArgumentNullException(nameof(activityProvider));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
    }

    public Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        AndroidQuickUnlockMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperatingSystem.IsAndroidVersionAtLeast(30)
            ? GetModernAvailability(mode)
            : PlatformQuickUnlockAvailability.NotSupported);
    }

    public async Task<AndroidBiometricPromptResult> AuthenticateAsync(
        AndroidQuickUnlockMode mode,
        Func<byte[]> completeCryptographicOperation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completeCryptographicOperation);
        cancellationToken.ThrowIfCancellationRequested();

        var activity = _activityProvider.GetCurrent();
        if (activity is null)
        {
            return AndroidBiometricPromptResult.Failed(
                PlatformQuickUnlockStatus.NotAvailable);
        }

        var completion = new TaskCompletionSource<AndroidBiometricPromptResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        BiometricPrompt? prompt = null;
        PromptCallback? callback = null;
        CancellationSignal? cancellationSignal = null;
        NegativeButtonListener? negativeButton = null;

        activity.RunOnUiThread(() =>
        {
            if (completion.Task.IsCompleted) return;
            try
            {
                var executor = activity.MainExecutor
                    ?? throw new InvalidOperationException(
                        "The Android main-thread executor is unavailable.");
                callback = new PromptCallback(completion, completeCryptographicOperation);
                negativeButton = new NegativeButtonListener(completion);
                using var builder = new BiometricPrompt.Builder(activity);
                builder.SetTitle(_strings.Get(mode == AndroidQuickUnlockMode.DeviceCredential
                    ? MobileStringKeys.DeviceCredentialPromptTitle
                    : MobileStringKeys.BiometricPromptTitle));
                builder.SetSubtitle(_strings.Get(mode == AndroidQuickUnlockMode.DeviceCredential
                    ? MobileStringKeys.DeviceCredentialPromptSubtitle
                    : MobileStringKeys.BiometricPromptSubtitle));
                if (mode == AndroidQuickUnlockMode.StrongBiometric)
                {
                    builder.SetNegativeButton(
                        _strings.Get(MobileStringKeys.BiometricUsePassword),
                        executor,
                        negativeButton);
                }
                if (OperatingSystem.IsAndroidVersionAtLeast(29))
                    builder.SetConfirmationRequired(false);
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    builder.SetAllowedAuthenticators(
                        GetAuthenticators(mode));
                }

                prompt = builder.Build();
                cancellationSignal = new CancellationSignal();
                prompt.Authenticate(
                    cancellationSignal,
                    executor,
                    callback);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        using var cancellation = cancellationToken.Register(() =>
        {
            activity.RunOnUiThread(() => cancellationSignal?.Cancel());
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            return await completion.Task;
        }
        finally
        {
            cancellationSignal?.Dispose();
            prompt?.Dispose();
            callback?.Dispose();
            negativeButton?.Dispose();
        }
    }

    [SupportedOSPlatform("android29.0")]
    private PlatformQuickUnlockAvailability GetModernAvailability(AndroidQuickUnlockMode mode)
    {
        var manager = _context.GetSystemService(Context.BiometricService) as BiometricManager;
        if (manager is null) return PlatformQuickUnlockAvailability.NotSupported;

        var result = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? manager.CanAuthenticate(
                GetAuthenticators(mode))
            : manager.CanAuthenticate();
        if ((int)result == SecurityUpdateRequiredErrorCode)
            return PlatformQuickUnlockAvailability.DisabledByPolicy;
        return result switch
        {
            BiometricCode.Success => PlatformQuickUnlockAvailability.Available,
            BiometricCode.ErrorNoneEnrolled =>
                PlatformQuickUnlockAvailability.NotConfigured,
            BiometricCode.ErrorNoHardware =>
                PlatformQuickUnlockAvailability.NotSupported,
            _ => PlatformQuickUnlockAvailability.TemporarilyUnavailable
        };
    }

    [SupportedOSPlatform("android30.0")]
    private static int GetAuthenticators(AndroidQuickUnlockMode mode) =>
        mode == AndroidQuickUnlockMode.DeviceCredential
            ? (int)BiometricManagerAuthenticators.DeviceCredential
            : (int)BiometricManagerAuthenticators.BiometricStrong;

    private sealed class PromptCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<AndroidBiometricPromptResult> _completion;
        private readonly Func<byte[]> _completeCryptographicOperation;

        public PromptCallback(
            TaskCompletionSource<AndroidBiometricPromptResult> completion,
            Func<byte[]> completeCryptographicOperation)
        {
            _completion = completion;
            _completeCryptographicOperation = completeCryptographicOperation;
        }

        public override void OnAuthenticationSucceeded(
            BiometricPrompt.AuthenticationResult? result)
        {
            try
            {
                var output = _completeCryptographicOperation();
                _completion.TrySetResult(AndroidBiometricPromptResult.Successful(output));
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public override void OnAuthenticationError(
            BiometricErrorCode errorCode,
            Java.Lang.ICharSequence? errString)
        {
            _completion.TrySetResult(AndroidBiometricPromptResult.Failed(
                MapError(errorCode)));
        }

        public override void OnAuthenticationFailed()
        {
            // Android keeps the prompt open for another biometric attempt.
        }

        private static PlatformQuickUnlockStatus MapError(
            BiometricErrorCode errorCode)
        {
            if ((int)errorCode == SecurityUpdateRequiredErrorCode)
                return PlatformQuickUnlockStatus.DisabledByPolicy;
            return errorCode switch
            {
                BiometricErrorCode.Canceled or
                    BiometricErrorCode.UserCanceled => PlatformQuickUnlockStatus.Cancelled,
                BiometricErrorCode.Lockout or
                    BiometricErrorCode.LockoutPermanent =>
                    PlatformQuickUnlockStatus.RetriesExhausted,
                BiometricErrorCode.NoBiometrics =>
                    PlatformQuickUnlockStatus.NotConfigured,
                BiometricErrorCode.HwNotPresent or
                    BiometricErrorCode.HwUnavailable => PlatformQuickUnlockStatus.NotAvailable,
                _ => PlatformQuickUnlockStatus.VerificationFailed
            };
        }
    }

    private sealed class NegativeButtonListener(
        TaskCompletionSource<AndroidBiometricPromptResult> completion)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which)
        {
            completion.TrySetResult(AndroidBiometricPromptResult.Failed(
                PlatformQuickUnlockStatus.Cancelled));
        }
    }
}
