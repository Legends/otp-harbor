using System.Reflection;
using System.Reflection.Emit;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class AssemblyApplicationVersionProviderTests
{
    [Fact]
    public void CurrentVersion_UsesFileVersionForStableAfterReleaseCandidateOrdering()
    {
        var assemblyName = new AssemblyName("SyntheticReleaseAssembly")
        {
            Version = new Version(2, 0, 0, 0)
        };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyFileVersionAttribute)
            .GetConstructor([typeof(string)]);
        Assert.NotNull(constructor);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            constructor,
            ["2.0.0.65535"]));

        var sut = new AssemblyApplicationVersionProvider(assembly);

        Assert.Equal(new Version(2, 0, 0, 65535), sut.CurrentVersion);
        Assert.True(sut.CurrentVersion > assemblyName.Version);
    }

    [Fact]
    public void CurrentVersion_WhenFileVersionIsUnavailable_FallsBackToAssemblyVersion()
    {
        var assemblyName = new AssemblyName("SyntheticFallbackAssembly")
        {
            Version = new Version(3, 1, 4, 2)
        };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);

        var sut = new AssemblyApplicationVersionProvider(assembly);

        Assert.Equal(assemblyName.Version, sut.CurrentVersion);
    }
}
