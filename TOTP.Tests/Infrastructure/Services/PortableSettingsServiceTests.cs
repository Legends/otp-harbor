using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class PortableSettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_WhenPreferencesAreMissing_UsesSafeDefaults()
    {
        var store = new Mock<IAppPreferencesStore>();
        store.Setup(value => value.LoadAsync(CancellationToken.None))
            .ReturnsAsync(Result.Ok<AppPreferencesV1?>(null));
        using var sut = CreateSut(store);

        var result = await sut.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Same(sut.Current, result.Value);
        Assert.Equal("en", result.Value.CultureName);
        Assert.Equal(PreferredUnlockMethod.Password, result.Value.PreferredUnlockMethod);
        Assert.True(result.Value.AppLockEnabled);
    }

    [Fact]
    public async Task LoadAsync_AppliesPortablePreferences()
    {
        var preferences = new AppPreferencesV1
        {
            CultureName = "de-DE",
            MinimumLogLevel = AppLogLevel.Warning,
            PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock,
            IdleTimeoutMinutes = 25,
            LockOnSessionLock = false,
            LockOnMinimize = false,
            ClearClipboardEnabled = false,
            ClearClipboardSeconds = 45,
            QrPreviewScaleFactor = 2.5,
            ExportEncrypt = false,
            OpenExportFileAfterExport = false,
            HideSecretsByDefault = false
        };
        var store = new Mock<IAppPreferencesStore>();
        store.Setup(value => value.LoadAsync(CancellationToken.None))
            .ReturnsAsync(Result.Ok<AppPreferencesV1?>(preferences));
        using var sut = CreateSut(store);

        var result = await sut.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(preferences.CultureName, result.Value.CultureName);
        Assert.Equal(preferences.MinimumLogLevel, result.Value.MinimumLogLevel);
        Assert.Equal(preferences.PreferredUnlockMethod, result.Value.PreferredUnlockMethod);
        Assert.Equal(TimeSpan.FromMinutes(25), result.Value.IdleTimeout);
        Assert.False(result.Value.LockOnSessionLock);
        Assert.False(result.Value.LockOnMinimize);
        Assert.False(result.Value.ClearClipboardEnabled);
        Assert.Equal(45, result.Value.ClearClipboardSeconds);
        Assert.Equal(2.5, result.Value.QrPreviewScaleFactor);
        Assert.False(result.Value.ExportEncrypt);
        Assert.False(result.Value.OpenExportFileAfterExport);
        Assert.False(result.Value.HideSecretsByDefault);
    }

    [Fact]
    public async Task LoadAsync_AfterSuccess_LoadsStoreOnlyOnceAndKeepsStableCurrentReference()
    {
        var store = new Mock<IAppPreferencesStore>();
        store.Setup(value => value.LoadAsync(CancellationToken.None))
            .ReturnsAsync(Result.Ok<AppPreferencesV1?>(new AppPreferencesV1()));
        using var sut = CreateSut(store);
        var initialReference = sut.Current;

        var first = await sut.LoadAsync();
        var second = await sut.LoadAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Same(initialReference, first.Value);
        Assert.Same(first.Value, second.Value);
        store.Verify(value => value.LoadAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_WhenStoreFails_PreservesTypedStoreFailureAndAllowsRetry()
    {
        var failure = new AppPreferencesError(
            AppPreferencesErrorCode.ReadFailed,
            "synthetic read failure");
        var store = new Mock<IAppPreferencesStore>();
        store.SetupSequence(value => value.LoadAsync(CancellationToken.None))
            .ReturnsAsync(Result.Fail<AppPreferencesV1?>(failure))
            .ReturnsAsync(Result.Ok<AppPreferencesV1?>(new AppPreferencesV1 { CultureName = "de-DE" }));
        using var sut = CreateSut(store);

        var first = await sut.LoadAsync();
        var second = await sut.LoadAsync();

        Assert.False(first.IsSuccess);
        Assert.Equal(
            AppPreferencesErrorCode.ReadFailed,
            Assert.Single(first.Errors.OfType<AppPreferencesError>()).Code);
        Assert.True(second.IsSuccess);
        Assert.Equal("de-DE", second.Value.CultureName);
        store.Verify(value => value.LoadAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task SaveAsync_MapsOnlyPortablePreferencesAndNeverSerializesAuthorization()
    {
        var store = new Mock<IAppPreferencesStore>();
        AppPreferencesV1? savedPreferences = null;
        store.Setup(value => value.SaveAsync(
                It.IsAny<AppPreferencesV1>(),
                CancellationToken.None))
            .Callback<AppPreferencesV1, CancellationToken>((preferences, _) => savedPreferences = preferences)
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(store);
        sut.Current.CultureName = "de-DE";
        sut.Current.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        var result = await sut.SaveAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(savedPreferences);
        Assert.Equal("de-DE", savedPreferences.CultureName);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, savedPreferences.PreferredUnlockMethod);
        Assert.DoesNotContain(
            typeof(AppPreferencesV1).GetProperties(),
            property => property.Name.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Wrapped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveAsync_WhenStoreFails_PreservesTypedStoreFailure()
    {
        var store = new Mock<IAppPreferencesStore>();
        store.Setup(value => value.SaveAsync(
                It.IsAny<AppPreferencesV1>(),
                CancellationToken.None))
            .ReturnsAsync(Result.Fail(new AppPreferencesError(
                AppPreferencesErrorCode.WriteFailed,
                "synthetic write failure")));
        using var sut = CreateSut(store);

        var result = await sut.SaveAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(
            AppPreferencesErrorCode.WriteFailed,
            Assert.Single(result.Errors.OfType<AppPreferencesError>()).Code);
    }

    private static PortableSettingsService CreateSut(Mock<IAppPreferencesStore> store) =>
        new(store.Object, NullLogger<PortableSettingsService>.Instance);
}
