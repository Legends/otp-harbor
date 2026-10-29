using TOTP.Camera.OpenCv;

namespace TOTP.Tests.Services;

public sealed class OpenCvNativeRuntimeProbeTests
{
    [Fact]
    public void Probe_LoadsAlignedWindowsNativeRuntime()
    {
        var result = OpenCvNativeRuntimeProbe.Probe();

        Assert.True(result.IsAvailable);
        Assert.StartsWith("4.13", result.Version, StringComparison.Ordinal);
    }
}
