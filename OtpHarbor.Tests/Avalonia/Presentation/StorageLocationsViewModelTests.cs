using System.Globalization;
using FluentResults;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.Storage;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class StorageLocationsViewModelTests
{
    [Fact]
    public void Refresh_ShowsOnlyTheAppDataDirectory()
    {
        using var model = new StorageLocationsViewModel(Locations().Object, Clipboard().Object, German);
        var item = Assert.Single(model.Locations);
        Assert.Equal(ApplicationStorageLocationKind.DataDirectory, item.Location.Kind);
        Assert.Equal(item.Path, item.Location.FolderPath);
    }

    [Fact]
    public void Refresh_WithoutAnAppDataDirectoryDoesNotShowOtherLocations()
    {
        var source = new Mock<IApplicationStorageLocations>();
        source.Setup(value => value.GetLocations()).Returns(new[]
        {
            new ApplicationStorageLocation(ApplicationStorageLocationKind.Vault, "demo/master.vault", "demo")
        });
        using var model = new StorageLocationsViewModel(source.Object, Clipboard().Object, German);
        Assert.Empty(model.Locations);
    }

    [Theory]
    [InlineData("en", "The path was copied.", "The path could not be copied to the clipboard.")]
    [InlineData("de", "Der Pfad wurde kopiert.", "Der Pfad konnte nicht in die Zwischenablage kopiert werden.")]
    [InlineData("fr", "Le chemin a été copié.", "Le chemin n’a pas pu être copié dans le presse-papiers.")]
    [InlineData("es", "Se copió la ruta.", "No se pudo copiar la ruta al portapapeles.")]
    public async Task CopyPathAsync_BothAppCatalogsProvideCompleteLocalizedFeedback(string language, string success, string failure)
    {
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(CultureInfo.GetCultureInfo(language));
        foreach (var localize in new Func<string, string>[]
                 { key => desktop.Get(key, CultureInfo.GetCultureInfo(language)), mobile.Get })
        {
            var clipboard = Clipboard();
            clipboard.Setup(value => value.CopyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
            using var model = new StorageLocationsViewModel(Locations().Object, clipboard.Object, localize);
            var item = model.Locations[0];
            await item.CopyPathAsync();
            Assert.Equal(success, item.StatusText);
            Assert.True(item.HasStatus);
            Assert.Equal(NotificationSeverity.Success, item.StatusSeverity);
            clipboard.Verify(value => value.CopyAsync(item.Path, It.IsAny<CancellationToken>()), Times.Once);

            clipboard.Setup(value => value.CopyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Fail("private OS failure detail"));
            await item.CopyPathAsync();
            Assert.Equal(failure, item.StatusText);
            Assert.Equal(NotificationSeverity.Error, item.StatusSeverity);
            Assert.DoesNotContain("private", item.StatusText);
        }
    }

    [Fact]
    public async Task Actions_ExceptionsAreRecoverableAndFolderOpeningUsesTheAppDataDirectory()
    {
        var clipboard = Clipboard();
        clipboard.Setup(value => value.CopyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private failure"));
        var folder = new Mock<IPlatformFolderLauncher>();
        folder.Setup(value => value.OpenFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private failure"));
        using var model = new StorageLocationsViewModel(Locations().Object, clipboard.Object, German, folder.Object);
        var item = model.Locations[0];
        await item.CopyPathAsync();
        Assert.Equal(German(StorageLocationStringKeys.CopyFailed), item.StatusText);
        Assert.True(item.CopyCommand.CanExecute(null));
        await item.OpenFolderAsync();
        Assert.Equal(German(StorageLocationStringKeys.FolderOpenFailed), item.StatusText);
        Assert.True(item.OpenFolderCommand.CanExecute(null));
        folder.Setup(value => value.OpenFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        await item.OpenFolderAsync();
        Assert.Equal(German(StorageLocationStringKeys.FolderOpened), item.StatusText);
        folder.Verify(value => value.OpenFolderAsync(item.Location.FolderPath!, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CopyPathAsync_BlocksOverlappingActionsAndIgnoresFeedbackForReplacedRows()
    {
        var clipboard = Clipboard();
        var completion = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Setup(value => value.CopyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(completion.Task);
        var source = Locations();
        using var model = new StorageLocationsViewModel(source.Object, clipboard.Object, German);
        var action = model.Locations[0].CopyPathAsync();
        Assert.All(model.Locations, item => Assert.False(item.CopyCommand.CanExecute(null)));
        await model.Locations[0].CopyPathAsync();
        clipboard.Verify(value => value.CopyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        source.Raise(value => value.Changed += null, EventArgs.Empty);
        completion.SetResult(Result.Ok());
        await action;
        Assert.All(model.Locations, item => Assert.False(item.HasStatus));
        Assert.All(model.Locations, item => Assert.True(item.CopyCommand.CanExecute(null)));
    }

    [Fact]
    public async Task RefreshLanguage_UpdatesLabelsAccessibilityAndExistingFeedback()
    {
        var language = CultureInfo.GetCultureInfo("en");
        var catalog = new AvaloniaStringCatalog();
        var clipboard = Clipboard();
        clipboard.Setup(value => value.CopyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        using var model = new StorageLocationsViewModel(Locations().Object, clipboard.Object, key => catalog.Get(key, language));
        var item = model.Locations[0];
        await item.CopyPathAsync();
        language = CultureInfo.GetCultureInfo("de");
        model.RefreshLanguage();
        Assert.Equal("App-Datenverzeichnis", item.Label);
        Assert.Equal("Pfad für App-Datenverzeichnis kopieren", item.CopyAccessibleName);
        Assert.Equal("Der Pfad wurde kopiert.", item.StatusText);
    }

    [Fact]
    public void WithoutFolderLauncher_ProvidesCopyingAndHidesFolderActions()
    {
        using var model = new StorageLocationsViewModel(Locations().Object, Clipboard().Object, German);
        Assert.All(model.Locations, item => Assert.True(item.CopyCommand.CanExecute(null)));
        Assert.All(model.Locations, item => Assert.False(item.CanOpenFolder));
        Assert.All(model.Locations, item => Assert.False(item.OpenFolderCommand.CanExecute(null)));
        model.Dispose();
        Assert.All(model.Locations, item => Assert.False(item.CopyCommand.CanExecute(null)));
    }

    private static Mock<IApplicationStorageLocations> Locations()
    {
        var root = Path.Combine(Path.GetTempPath(), "otp-storage-demo");
        var source = new Mock<IApplicationStorageLocations>();
        source.Setup(value => value.GetLocations()).Returns(new[]
        {
            new ApplicationStorageLocation(ApplicationStorageLocationKind.DataDirectory, root, root),
            new ApplicationStorageLocation(ApplicationStorageLocationKind.Vault, Path.Combine(root, "master.vault"), root),
            new ApplicationStorageLocation(ApplicationStorageLocationKind.LogDirectory, Path.Combine(root, "logs"), Path.Combine(root, "logs"))
        });
        return source;
    }

    private static Mock<IAsyncClipboardService> Clipboard()
    {
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.SetupGet(value => value.Capabilities).Returns(ClipboardCapabilities.WriteText);
        return clipboard;
    }

    private static string German(string key) => new AvaloniaStringCatalog().Get(key, CultureInfo.GetCultureInfo("de"));
}
