using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Security.Models;
using TOTP.Platform.MacOS.Security;

namespace TOTP.Tests.Platform.MacOS.Security;

public sealed class MacOSKeychainSecretStoreTests
{
    [Fact]
    public async Task StoreAndRetrieve_UsesNativeBoundaryWithoutReturningNativeBuffer()
    {
        var native = new FakeNative();
        var sut = CreateSut(native);
        var source = new byte[] { 1, 2, 3, 4 };

        var stored = await sut.StoreAsync("reference", source, TestContext.Current.CancellationToken);
        source.AsSpan().Clear();
        native.ReadSecret = new byte[] { 1, 2, 3, 4 };
        var nativeBuffer = native.ReadSecret;
        var retrieved = await sut.RetrieveAsync("reference", TestContext.Current.CancellationToken);

        Assert.True(stored.IsSuccess);
        Assert.True(retrieved.IsSuccess);
        using var secret = Assert.IsType<SensitiveBuffer>(retrieved.Value);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, secret.Memory.ToArray());
        Assert.All(nativeBuffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Retrieve_WhenNativeItemIsMissing_ReturnsSuccessfulNull()
    {
        var native = new FakeNative { ReadStatus = MacOSKeychainNativeStatus.NotFound };
        var result = await CreateSut(native).RetrieveAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Store_WhenNativeAccessIsDenied_ReturnsTypedSanitizedFailure()
    {
        var native = new FakeNative { StoreStatus = MacOSKeychainNativeStatus.AccessDenied };
        var result = await CreateSut(native).StoreAsync(
            "reference",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<PlatformSecretStoreError>(Assert.Single(result.Errors));
        Assert.Equal(PlatformSecretStoreErrorCode.AccessDenied, error.Code);
        Assert.DoesNotContain("reference", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nreference")]
    public async Task Operations_RejectInvalidReferenceBeforeNativeCall(string reference)
    {
        var native = new FakeNative();
        var sut = CreateSut(native);

        var result = await sut.DeleteAsync(reference, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(0, native.DeleteCount);
    }

    private static MacOSKeychainSecretStore CreateSut(IMacOSKeychainNative native) =>
        new(native, Mock.Of<ILogger<MacOSKeychainSecretStore>>());

    private sealed class FakeNative : IMacOSKeychainNative
    {
        public MacOSKeychainNativeStatus StoreStatus { get; init; } = MacOSKeychainNativeStatus.Success;
        public MacOSKeychainNativeStatus ReadStatus { get; init; } = MacOSKeychainNativeStatus.Success;
        public byte[]? ReadSecret { get; set; }
        public int DeleteCount { get; private set; }

        public PlatformSecretStoreAvailability GetAvailability() => PlatformSecretStoreAvailability.Available;

        public MacOSKeychainNativeStatus Store(string secretReference, ReadOnlyMemory<byte> secret) =>
            StoreStatus;

        public MacOSKeychainReadResult Retrieve(string secretReference) =>
            new(ReadStatus, ReadSecret);

        public MacOSKeychainNativeStatus Delete(string secretReference)
        {
            DeleteCount++;
            return MacOSKeychainNativeStatus.Success;
        }
    }
}
