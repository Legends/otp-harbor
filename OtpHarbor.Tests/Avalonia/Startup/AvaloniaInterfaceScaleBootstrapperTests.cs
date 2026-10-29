using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Models;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaInterfaceScaleBootstrapperTests
{
    [Fact]
    public void ResolveMultiplier_SystemDefault_LeavesPlatformScalingUntouched()
    {
        var result = AvaloniaInterfaceScaleBootstrapper.ResolveMultiplier(new AppPreferencesV1());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(100, 1.0)]
    [InlineData(175, 1.75)]
    [InlineData(300, 3.0)]
    public void ResolveMultiplier_CustomScale_IsRelativeToPlatformScaling(
        int percent,
        double expected)
    {
        var result = AvaloniaInterfaceScaleBootstrapper.ResolveMultiplier(new AppPreferencesV1
        {
            InterfaceScalePercent = percent
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveMultiplier_UnreviewedScale_FailsBackToSystemScaling()
    {
        var result = AvaloniaInterfaceScaleBootstrapper.ResolveMultiplier(new AppPreferencesV1
        {
            InterfaceScalePercent = 110
        });

        Assert.Null(result);
    }
}
