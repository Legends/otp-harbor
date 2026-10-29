using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;
using TOTP.DAL.Services;
using TOTP.Infrastructure.Security;
using TOTP.Tests.Common;

namespace TOTP.Tests.Integration;

public sealed class StoredVaultKeyVerifierTests
{
    [Fact]
    public async Task VerifyAsync_WhenVaultIsMissing_ReturnsFirstRunOutcome()
    {
        using var temp = new TempDir();
        using var sut = CreateVerifier(Path.Combine(temp.Path, "master.totp"));

        var result = await sut.VerifyAsync(
            new byte[32],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(VaultKeyVerificationStatus.VaultNotFound, result.Value);
    }

    [Fact]
    public async Task VerifyAsync_WithMatchingKey_ReturnsVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "master.totp");
        var key = RandomNumberGenerator.GetBytes(32);
        await File.WriteAllBytesAsync(path, CreateVault(key), cancellationToken);
        using var sut = CreateVerifier(path);

        var result = await sut.VerifyAsync(key, cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(VaultKeyVerificationStatus.Verified, result.Value);
    }

    [Fact]
    public async Task VerifyAsync_WithWrongKey_ReturnsAuthenticationFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "master.totp");
        await File.WriteAllBytesAsync(
            path,
            CreateVault(RandomNumberGenerator.GetBytes(32)),
            cancellationToken);
        using var sut = CreateVerifier(path);

        var result = await sut.VerifyAsync(RandomNumberGenerator.GetBytes(32), cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(VaultKeyVerificationStatus.AuthenticationFailed, result.Value);
    }

    [Fact]
    public async Task VerifyAsync_WhenVaultExceedsBound_ReturnsTypedFailure()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "master.totp");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(StoredVaultKeyVerifier.MaximumVaultSize + 1L);
        }
        using var sut = CreateVerifier(path);

        var result = await sut.VerifyAsync(
            new byte[32],
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(StoredVaultVerificationErrorCode.TooLarge, ErrorCode(result.Errors));
    }

    [Fact]
    public async Task VerifyAsync_WhenFileProtectionFails_ReturnsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "master.totp");
        await File.WriteAllBytesAsync(path, [1], cancellationToken);
        using var sut = new StoredVaultKeyVerifier(
            path,
            new VaultService(Mock.Of<TOTP.Core.Security.Interfaces.ISecurityContext>()),
            NullLogger<StoredVaultKeyVerifier>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = _ => throw new UnauthorizedAccessException("denied")
            });

        var result = await sut.VerifyAsync(new byte[32], cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(StoredVaultVerificationErrorCode.ReadAccessDenied, ErrorCode(result.Errors));
    }

    [Fact]
    public async Task VerifyAsync_WhenVaultPathIsDirectory_DoesNotReportFirstRun()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "master.totp");
        Directory.CreateDirectory(path);
        using var sut = CreateVerifier(path);

        var result = await sut.VerifyAsync(
            new byte[32],
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(StoredVaultVerificationErrorCode.ReadAccessDenied, ErrorCode(result.Errors));
    }

    [Fact]
    public async Task VerifyAsync_WhenCancelled_PropagatesCancellation()
    {
        using var temp = new TempDir();
        using var sut = CreateVerifier(Path.Combine(temp.Path, "master.totp"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.VerifyAsync(new byte[32], cancellation.Token));
    }

    private static StoredVaultKeyVerifier CreateVerifier(string path) => new(
        path,
        new VaultService(Mock.Of<TOTP.Core.Security.Interfaces.ISecurityContext>()),
        NullLogger<StoredVaultKeyVerifier>.Instance,
        NoOpPlatformFileSecurity.Instance);

    private static byte[] CreateVault(byte[] key)
    {
        using var security = new SecurityContext();
        security.SetDek(key);
        return new VaultService(security).EncryptVault(
            [new Account(Guid.NewGuid(), "Synthetic", "TESTSECRET")]);
    }

    private static StoredVaultVerificationErrorCode ErrorCode(
        IEnumerable<FluentResults.IError> errors) =>
        Assert.IsType<StoredVaultVerificationError>(Assert.Single(errors)).Code;

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"totp-stored-vault-verifier-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
