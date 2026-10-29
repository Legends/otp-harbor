using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Platform;
using TOTP.Platform.Linux;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Platform.Linux;

public sealed class LinuxSessionEventSourceTests
{
    [Fact]
    public void ProcessLine_MapsSupportedScreenSaverSignalsAndSuppressesDuplicates()
    {
        var sut = CreateSut(new FakeRuntime());
        var states = new List<PlatformSessionState>();
        sut.SessionChanged += (_, args) => states.Add(args.State);

        sut.ProcessLine("signal time=1 interface=org.freedesktop.ScreenSaver; member=ActiveChanged");
        sut.ProcessLine("   boolean true");
        sut.ProcessLine("signal time=2 interface=org.gnome.ScreenSaver; member=ActiveChanged");
        sut.ProcessLine("   boolean true");
        sut.ProcessLine("signal time=3 interface=org.gnome.ScreenSaver; member=ActiveChanged");
        sut.ProcessLine("   boolean false");

        Assert.Equal([PlatformSessionState.Locked, PlatformSessionState.Active], states);
    }

    [Fact]
    public void ProcessLine_IgnoresUnrelatedBooleanPayload()
    {
        var sut = CreateSut(new FakeRuntime());
        var raised = false;
        sut.SessionChanged += (_, _) => raised = true;

        sut.ProcessLine("signal time=1 interface=org.example.Other; member=ActiveChanged");
        sut.ProcessLine("   boolean true");

        Assert.False(raised);
    }

    [Fact]
    public void StartAndStop_ManageOneMonitorLifetime()
    {
        var runtime = new FakeRuntime();
        var sut = CreateSut(runtime);

        sut.Start();
        sut.Start();
        sut.Stop();
        sut.Stop();

        Assert.Equal(1, runtime.StartCount);
        Assert.Equal(1, runtime.Monitor.DisposeCount);
    }

    private static LinuxSessionEventSource CreateSut(ILinuxSessionMonitorRuntime runtime) =>
        new(runtime, Mock.Of<ILogger<LinuxSessionEventSource>>());

    private sealed class FakeRuntime : ILinuxSessionMonitorRuntime
    {
        public bool IsSupported { get; init; } = true;
        public PlatformCapabilityStatus CapabilityStatus => IsSupported
            ? PlatformCapabilityStatus.Supported
            : PlatformCapabilityStatus.PermanentlyUnavailable;
        public int StartCount { get; private set; }
        public FakeMonitor Monitor { get; } = new();

        public ILinuxSessionMonitor Start(Action<string> onOutputLine, Action onExited)
        {
            StartCount++;
            return Monitor;
        }
    }

    private sealed class FakeMonitor : ILinuxSessionMonitor
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
