using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AppearanceSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"otp-appearance-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Constructor_WithoutStoredPreference_UsesDarkTheme()
    {
        var sut = CreateSut();

        Assert.Equal(AppThemePreference.Dark, sut.ThemePreference);
    }

    [Fact]
    public async Task SetThemePreferenceAsync_PersistsAndReloadsLightTheme()
    {
        var sut = CreateSut();
        var changed = 0;
        sut.PreferenceChanged += (_, _) => changed++;

        var result = await sut.SetThemePreferenceAsync(
            AppThemePreference.Light,
            TestContext.Current.CancellationToken);
        var reloaded = CreateSut();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, changed);
        Assert.Equal(AppThemePreference.Light, reloaded.ThemePreference);
        var json = await File.ReadAllTextAsync(
            Path.Combine(_root, "appearance-settings.json"),
            TestContext.Current.CancellationToken);
        Assert.Equal("{\"version\":1,\"theme\":\"Light\"}", json);
    }

    [Fact]
    public async Task Constructor_WithMalformedPreference_UsesDarkTheme()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "appearance-settings.json"),
            "{\"version\":1,\"theme\":\"unknown\"}",
            TestContext.Current.CancellationToken);

        var sut = CreateSut();

        Assert.Equal(AppThemePreference.Dark, sut.ThemePreference);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AppearanceSettingsService CreateSut()
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(_root);
        return new AppearanceSettingsService(
            paths.Object,
            Mock.Of<IPlatformFileSecurity>(),
            NullLogger<AppearanceSettingsService>.Instance);
    }
}
