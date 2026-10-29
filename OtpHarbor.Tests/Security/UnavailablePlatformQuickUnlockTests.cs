using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class UnavailablePlatformQuickUnlockTests
{
    private readonly UnavailablePlatformQuickUnlock _sut = new();

    [Fact]
    public async Task GetAvailabilityAsync_ReportsNotSupported()
    {
        var availability = await _sut.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PlatformQuickUnlockAvailability.NotSupported, availability);
        Assert.False(string.Equals(
            PlatformQuickUnlockContract.WindowsHelloTpmProvider,
            _sut.ProviderId,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterAsync_FailsWithTypedUnavailableError()
    {
        var result = await _sut.RegisterAsync(
            new byte[32],
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<PlatformQuickUnlockError>(Assert.Single(result.Errors));
        Assert.Equal(PlatformQuickUnlockErrorCode.Unavailable, error.Code);
    }

    [Fact]
    public async Task ExistingWrapperOperations_FailWithTypedUnavailableError()
    {
        var wrapper = CreateWrapper();

        var unlock = await _sut.TryUnlockAsync(
            wrapper,
            TestContext.Current.CancellationToken);
        var remove = await _sut.RemoveAsync(
            wrapper,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            PlatformQuickUnlockErrorCode.Unavailable,
            Assert.IsType<PlatformQuickUnlockError>(Assert.Single(unlock.Errors)).Code);
        Assert.Equal(
            PlatformQuickUnlockErrorCode.Unavailable,
            Assert.IsType<PlatformQuickUnlockError>(Assert.Single(remove.Errors)).Code);
    }

    private static PlatformQuickUnlockWrapperV2 CreateWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.WindowsHelloTpmProvider,
        ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "test-key",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
            Ciphertext = new byte[256]
        }
    };
}
