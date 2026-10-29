using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Security.Models;
using TOTP.Platform.Linux.Security;

namespace TOTP.Tests.Platform.Linux.Security;

public sealed class LinuxSecretServiceStoreTests
{
    [Fact]
    public async Task StoreAsync_PassesSecretOnlyAsBase64StandardInput()
    {
        var runtime = new FakeRuntime();
        var sut = CreateSut(runtime);
        var secret = new byte[] { 0, 1, 2, 255 };

        var result = await sut.StoreAsync("opaque-reference", secret, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("AAEC/w==\n", Encoding.ASCII.GetString(runtime.LastInput!));
        Assert.DoesNotContain(runtime.LastArguments!, value => value.Contains("AAEC", StringComparison.Ordinal));
        Assert.DoesNotContain(runtime.LastArguments!, value => value.Contains("0,1,2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetrieveAsync_DecodesIntoCallerOwnedBufferAndClearsCommandOutput()
    {
        var commandOutput = Encoding.ASCII.GetBytes("AQIDBA==\n");
        var runtime = new FakeRuntime
        {
            NextResult = new LinuxSecretServiceCommandResult(0, commandOutput)
        };
        var sut = CreateSut(runtime);

        var result = await sut.RetrieveAsync("opaque-reference", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var secret = Assert.IsType<SensitiveBuffer>(result.Value);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, secret.Memory.ToArray());
        Assert.All(commandOutput, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task RetrieveAsync_WhenLookupHasNoMatch_ReturnsSuccessfulNull()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new LinuxSecretServiceCommandResult(1, [])
        };

        var result = await CreateSut(runtime).RetrieveAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task RetrieveAsync_WhenOutputIsNotBase64_FailsWithoutReturningData()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new LinuxSecretServiceCommandResult(0, Encoding.ASCII.GetBytes("not-base64\n"))
        };

        var result = await CreateSut(runtime).RetrieveAsync(
            "reference",
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<PlatformSecretStoreError>(Assert.Single(result.Errors));
        Assert.Equal(PlatformSecretStoreErrorCode.InvalidSecret, error.Code);
    }

    [Fact]
    public async Task Operations_WhenSessionBusIsMissing_FailClosedBeforeProcessStart()
    {
        var runtime = new FakeRuntime { HasSessionBus = false };

        var result = await CreateSut(runtime).StoreAsync(
            "reference",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<PlatformSecretStoreError>(Assert.Single(result.Errors));
        Assert.Equal(PlatformSecretStoreErrorCode.Unavailable, error.Code);
        Assert.Equal(0, runtime.CallCount);
    }

    [Fact]
    public async Task DeleteAsync_TreatsAbsentItemAsSuccess()
    {
        var runtime = new FakeRuntime
        {
            NextResult = new LinuxSecretServiceCommandResult(1, [])
        };

        var result = await CreateSut(runtime).DeleteAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    private static LinuxSecretServiceStore CreateSut(ILinuxSecretServiceRuntime runtime) =>
        new(runtime, Mock.Of<ILogger<LinuxSecretServiceStore>>());

    private sealed class FakeRuntime : ILinuxSecretServiceRuntime
    {
        public bool IsPlatformSupported { get; init; } = true;
        public bool HasSessionBus { get; set; } = true;
        public string? SecretToolPath { get; init; } = "/usr/bin/secret-tool";
        public LinuxSecretServiceCommandResult NextResult { get; init; } = new(0, []);
        public int CallCount { get; private set; }
        public string[]? LastArguments { get; private set; }
        public byte[]? LastInput { get; private set; }

        public Task<LinuxSecretServiceCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            ReadOnlyMemory<byte> standardInput,
            int maximumOutputBytes,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastArguments = arguments.ToArray();
            LastInput = standardInput.ToArray();
            return Task.FromResult(NextResult);
        }
    }
}
