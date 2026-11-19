using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Security.Models;
using TOTP.Platform.Windows.Security;

namespace TOTP.Tests.Platform.Windows.Security;

public sealed class WindowsCngUnattendedUnlockTests
{
    [Fact]
    public async Task NativeUserKey_RoundTripTamperAndDeletionPreserveNonExportablePolicy()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sut = new WindowsCngUnattendedUnlock(NullLogger<WindowsCngUnattendedUnlock>.Instance);
        var source = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var registered = await sut.RegisterAsync(source, TestContext.Current.CancellationToken);
        Assert.True(registered.IsSuccess);
        var wrapper = registered.Value;
        try
        {
            Assert.True(PlatformQuickUnlockContract.IsSupportedUnattendedWrapper(wrapper));
            using (var key = CngKey.Open(wrapper.KeyReference, CngProvider.MicrosoftSoftwareKeyStorageProvider))
            {
                Assert.False(key.IsMachineKey);
                Assert.Equal(CngExportPolicies.None, key.ExportPolicy);
                Assert.Throws<CryptographicException>(() => key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob));
            }
            using var recovered = (await sut.TryUnlockAsync(wrapper, TestContext.Current.CancellationToken)).Value;
            Assert.Equal(source, recovered.VaultKey!.Memory.ToArray());
            var tampered = wrapper with { WrappedKey = wrapper.WrappedKey with { Ciphertext = new byte[256] } };
            Assert.False((await sut.TryUnlockAsync(tampered, TestContext.Current.CancellationToken)).IsSuccess);
        }
        finally { Assert.True((await sut.RemoveAsync(wrapper, TestContext.Current.CancellationToken)).IsSuccess); }
        using var missing = (await sut.TryUnlockAsync(wrapper, TestContext.Current.CancellationToken)).Value;
        Assert.Equal(PlatformQuickUnlockStatus.KeyNotFound, missing.Status);
        Assert.True((await sut.RemoveAsync(wrapper, TestContext.Current.CancellationToken)).IsSuccess);
    }
}
