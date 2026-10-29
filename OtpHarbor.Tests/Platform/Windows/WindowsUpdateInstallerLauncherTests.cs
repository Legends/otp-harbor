using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Platform.Windows;

namespace TOTP.Tests.Platform.Windows;

public sealed class WindowsUpdateInstallerLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"totp-windows-installer-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LaunchAsync_ReverifiesPackage_StartsTrustedHelper_ThenShutsDown()
    {
        var fixture = CreateFixture();
        var verifier = new RecordingVerifier { Result = true };
        var lifetime = new Mock<IApplicationLifetime>();
        fixture.Runtime.OnStart = startInfo =>
        {
            fixture.Runtime.StartInfo = startInfo;
            var arguments = startInfo.ArgumentList.ToArray();
            File.WriteAllText(arguments[Array.IndexOf(arguments, "--readySignalPath") + 1], "ready");
            return true;
        };
        var sut = CreateSut(verifier, lifetime.Object, fixture.Runtime);

        var result = await sut.LaunchAsync(fixture.Package, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.PackageBytes, verifier.Payload);
        Assert.Equal(fixture.Package.ExpectedSignature, verifier.Signature);
        Assert.Equal(fixture.Package.PublicKey, verifier.PublicKey);
        lifetime.Verify(value => value.Shutdown(0), Times.Once);
        Assert.NotNull(fixture.Runtime.StartInfo);
        Assert.False(fixture.Runtime.StartInfo!.UseShellExecute);
        Assert.Equal("TOTP.Updater.exe", Path.GetFileName(fixture.Runtime.StartInfo.FileName));
        var actualArguments = fixture.Runtime.StartInfo.ArgumentList.ToArray();
        Assert.Equal(fixture.Package.FilePath, ValueAfter(actualArguments, "--packagePath"));
        Assert.Equal(fixture.Runtime.CurrentExecutablePath, ValueAfter(actualArguments, "--exePath"));
        Assert.True(File.Exists(fixture.Runtime.StartInfo.FileName));
    }

    [Fact]
    public async Task LaunchAsync_WhenHandoffSignatureFails_DoesNotStartOrShutdown()
    {
        var fixture = CreateFixture();
        var verifier = new RecordingVerifier { Result = false };
        var lifetime = new Mock<IApplicationLifetime>();
        var sut = CreateSut(verifier, lifetime.Object, fixture.Runtime);

        var result = await sut.LaunchAsync(fixture.Package, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Null(fixture.Runtime.StartInfo);
        lifetime.Verify(value => value.Shutdown(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LaunchAsync_RejectsNonZipPackageBeforeVerification()
    {
        var fixture = CreateFixture("update.ready.exe");
        var verifier = new RecordingVerifier();
        var lifetime = new Mock<IApplicationLifetime>();
        var sut = CreateSut(verifier, lifetime.Object, fixture.Runtime);

        var result = await sut.LaunchAsync(fixture.Package, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(0, verifier.CallCount);
        Assert.Null(fixture.Runtime.StartInfo);
    }

    [Fact]
    public async Task LaunchAsync_RejectsExecutableOutsideInstallationDirectory()
    {
        var fixture = CreateFixture();
        var outsideDirectory = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outsideDirectory);
        fixture.Runtime.CurrentExecutablePath = Path.Combine(outsideDirectory, "TOTP.exe");
        File.WriteAllText(fixture.Runtime.CurrentExecutablePath, "application");
        var verifier = new RecordingVerifier();
        var lifetime = new Mock<IApplicationLifetime>();
        var sut = CreateSut(verifier, lifetime.Object, fixture.Runtime);

        var result = await sut.LaunchAsync(fixture.Package, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(0, verifier.CallCount);
        Assert.Null(fixture.Runtime.StartInfo);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private Fixture CreateFixture(string packageName = "update.ready.zip")
    {
        var installDirectory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "app");
        var tempDirectory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "temp");
        var updateDirectory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "updates");
        Directory.CreateDirectory(Path.Combine(installDirectory, "TOTP.Updater"));
        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(updateDirectory);
        var executablePath = Path.Combine(installDirectory, "TOTP.UI.Avalonia.Desktop.exe");
        File.WriteAllText(executablePath, "application");
        File.WriteAllText(
            Path.Combine(installDirectory, "TOTP.Updater", "TOTP.Updater.exe"),
            "updater");
        var packageBytes = new byte[] { 1, 3, 3, 7 };
        var packagePath = Path.Combine(updateDirectory, packageName);
        File.WriteAllBytes(packagePath, packageBytes);
        var runtime = new FakeRuntime
        {
            InstallationDirectory = installDirectory,
            CurrentExecutablePath = executablePath,
            TemporaryDirectory = tempDirectory,
            CurrentProcessId = 42
        };
        return new Fixture(
            new PortableUpdatePackage(new Version(2, 0), packagePath, "signature", "public-key"),
            packageBytes,
            runtime);
    }

    private static WindowsUpdateInstallerLauncher CreateSut(
        ISignedPayloadVerifier verifier,
        IApplicationLifetime lifetime,
        IWindowsUpdateInstallerRuntime runtime) =>
        new(verifier, lifetime, runtime, Mock.Of<ILogger<WindowsUpdateInstallerLauncher>>());

    private static string ValueAfter(IReadOnlyList<string> arguments, string name)
    {
        var index = arguments.ToList().IndexOf(name);
        Assert.True(index >= 0 && index + 1 < arguments.Count);
        return arguments[index + 1];
    }

    private sealed record Fixture(
        PortableUpdatePackage Package,
        byte[] PackageBytes,
        FakeRuntime Runtime);

    private sealed class FakeRuntime : IWindowsUpdateInstallerRuntime
    {
        public required string InstallationDirectory { get; init; }
        public required string? CurrentExecutablePath { get; set; }
        public required int CurrentProcessId { get; init; }
        public required string TemporaryDirectory { get; init; }
        public Func<ProcessStartInfo, bool>? OnStart { get; set; }
        public ProcessStartInfo? StartInfo { get; set; }

        public bool Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return OnStart?.Invoke(startInfo) ?? false;
        }
    }

    private sealed class RecordingVerifier : ISignedPayloadVerifier
    {
        public bool Result { get; init; }
        public int CallCount { get; private set; }
        public byte[]? Payload { get; private set; }
        public string? Signature { get; private set; }
        public string? PublicKey { get; private set; }

        public bool Verify(ReadOnlySpan<byte> payload, string signature, string publicKey)
        {
            CallCount++;
            Payload = payload.ToArray();
            Signature = signature;
            PublicKey = publicKey;
            return Result;
        }
    }
}
