using FluentResults;
using System.Diagnostics;
using Moq;
using Avalonia.Controls;
using Avalonia.Media;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class AccountListViewModelTests
{
    private const string ValidSecret = "JBSWY3DPEHPK3PXP";

    [Fact]
    public async Task LoadAsync_WithNoStoredAccounts_ExposesEmptyState()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        Assert.True(sut.HasNoAccounts);
        Assert.False(sut.HasNoSearchResults);
        Assert.False(sut.HasMessage);
    }

    [Fact]
    public async Task RevealImportedAccountAsync_SelectsHighlightsAndAnnouncesImportedRow()
    {
        var importedId = Guid.NewGuid();
        var accounts = Enumerable.Range(0, 30)
            .Select(index => new Account(
                index == 29 ? importedId : Guid.NewGuid(),
                $"Issuer {index:00}",
                ValidSecret,
                $"account-{index:00}"))
            .ToArray();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(accounts));
        using var sut = CreateSut(manager.Object);

        await sut.RevealImportedAccountAsync(
            importedId,
            highlightAsNew: true,
            AvaloniaStringKeys.QrAccountAdded);

        Assert.Equal(importedId, sut.SelectedAccount!.Id);
        Assert.True(sut.SelectedAccount.IsRecentlyAdded);
        Assert.Equal(AvaloniaStringKeys.QrAccountAdded, sut.Message);

        sut.SearchText = "does-not-match";
        sut.SearchText = string.Empty;

        Assert.Null(sut.SelectedAccount);
        Assert.False(sut.Accounts.Single(account => account.Id == importedId).IsRecentlyAdded);
    }

    [Fact]
    public async Task SearchText_WithNoMatches_ExposesFilteredEmptyState()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(Guid.NewGuid(), "GitHub", ValidSecret, "alice")
            ]));
        var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        sut.SearchText = "missing";

        Assert.False(sut.HasNoAccounts);
        Assert.True(sut.HasNoSearchResults);
    }

    [Fact]
    public async Task LoadAsync_WithFiveHundredSyntheticAccounts_ProjectsSecretFreeRows()
    {
        var accounts = Enumerable.Range(1, 500)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index:D3}",
                $"SYNTHETIC-SECRET-{index:D3}",
                $"user{index:D3}@example.test"))
            .ToArray();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(accounts));
        var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        Assert.Equal(500, sut.Accounts.Count);
        Assert.Equal("Issuer 001", sut.Accounts[0].Issuer);
        Assert.Equal("user500@example.test", sut.Accounts[^1].AccountName);
        Assert.DoesNotContain(
            typeof(AccountListItemViewModel).GetProperties(),
            property => string.Equals(property.Name, "Secret", StringComparison.Ordinal));
        Assert.False(sut.HasMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenAutomaticGenerationIsEnabled_ProjectsCodeIntoEveryRow()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(firstId, "First", ValidSecret, "alice"),
                new(secondId, "Second", ValidSecret, "bob")
            ]));
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateManyAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Result.Ok(new AccountTotpGenerationBatch(
                new Dictionary<Guid, TotpGenerationResult>
                {
                    [firstId] = new("111111", 25, 30),
                    [secondId] = new("222222", 25, 30)
                },
                new HashSet<Guid>())));
        using var sut = new AccountListViewModel(
            manager.Object,
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        sut.EnableAutomaticCodeGenerationOnSelection();

        await sut.LoadAsync();
        await WaitUntilAsync(() => sut.Accounts.All(account => account.HasCode));

        Assert.Collection(
            sut.Accounts,
            first => Assert.Equal("111 111", first.DisplayCode),
            second => Assert.Equal("222 222", second.DisplayCode));
        Assert.All(sut.Accounts, account => Assert.Equal(30, account.PeriodSeconds));
        totp.Verify(value => value.GenerateManyAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2)), Times.Once);

        sut.ClearSensitiveOutput();

        Assert.All(sut.Accounts, account => Assert.False(account.HasCode));
    }

    [Fact]
    public async Task CopyAccountCodeAsync_CopiesRequestedRowWithoutChangingSelection()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(firstId, "First", ValidSecret, "alice"),
                new(secondId, "Second", ValidSecret, "bob")
            ]));
        var clipboard = SuccessfulClipboard();
        using var sut = new AccountListViewModel(
            manager.Object,
            Mock.Of<IAccountTotpService>(),
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];
        sut.Accounts[1].UpdateCode("222222", 12, 30);

        await sut.CopyAccountCodeAsync(sut.Accounts[1]);

        Assert.Equal(firstId, sut.SelectedAccount.Id);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "222222",
            TimeSpan.FromSeconds(12),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TenThousandAccountProjectionAndFiltering_RemainsWithinDesktopBudget()
    {
        var accounts = Enumerable.Range(1, 10_000)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index:D5}",
                ValidSecret,
                $"user{index:D5}@example.test"))
            .ToArray();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(accounts));
        var sut = CreateSut(manager.Object);
        var stopwatch = Stopwatch.StartNew();

        await sut.LoadAsync();
        sut.SearchText = "user09999";
        stopwatch.Stop();

        Assert.Single(sut.Accounts);
        Assert.Equal("user09999@example.test", sut.Accounts[0].AccountName);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Projection and filtering took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task LoadAsync_WhenManagerFails_ClearsRowsAndShowsRecoverableMessage()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Fail<IReadOnlyList<Account>>("synthetic failure"));
        var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        Assert.Empty(sut.Accounts);
        Assert.Contains("not changed", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchText_FiltersIssuerAndAccountNameCaseInsensitively()
    {
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", "SECRET-A", "alice@example.test"),
            new(Guid.NewGuid(), "Microsoft", "SECRET-B", "bob@example.test"),
            new(Guid.NewGuid(), "Example", "SECRET-C", "github-user@example.test")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        sut.SearchText = "GITHUB";

        Assert.Equal(2, sut.Accounts.Count);
        Assert.Contains(sut.Accounts, account => account.Issuer == "GitHub");
        Assert.Contains(sut.Accounts, account => account.AccountName == "github-user@example.test");

        sut.SearchText = "  bob  ";
        Assert.Single(sut.Accounts);
        Assert.Equal("Microsoft", sut.Accounts[0].Issuer);

        sut.SearchText = string.Empty;
        Assert.Equal(3, sut.Accounts.Count);
    }

    [Fact]
    public async Task SearchText_WhenSelectedAccountIsFilteredOut_ClearsSelectionAndGeneratedCode()
    {
        var selectedId = Guid.NewGuid();
        IReadOnlyList<Account> accounts =
        [
            new(selectedId, "GitHub", ValidSecret, "alice@example.test"),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob@example.test")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(selectedId))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        var clipboard = SuccessfulClipboard();
        using var sut = new AccountListViewModel(
            manager.Object,
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        await sut.LoadAsync();
        sut.EnableAutomaticCodeGenerationOnSelection();
        sut.SelectedAccount = sut.Accounts.Single(account => account.Id == selectedId);
        await WaitUntilAsync(() => sut.GeneratedCode == "123456");

        sut.SearchText = "Microsoft";

        Assert.Null(sut.SelectedAccount);
        Assert.False(sut.HasSelectedAccount);
        Assert.Empty(sut.GeneratedCode);
        Assert.Equal(0, sut.RemainingSeconds);
        Assert.Equal(0, sut.PeriodSeconds);
    }

    [Fact]
    public async Task LoadAsync_WhenBoundaryThrows_DoesNotExposeExceptionText()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ThrowsAsync(new InvalidOperationException("sensitive synthetic detail"));
        var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        Assert.Empty(sut.Accounts);
        Assert.DoesNotContain("sensitive", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateCodeAsync_UsesSelectedIdAndProjectsExpiringCode()
    {
        var accountId = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(accountId))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization())
        {
            SelectedAccount = new AccountListItemViewModel(accountId, "Issuer", "account")
        };

        await sut.GenerateCodeAsync();

        Assert.Equal("123456", sut.GeneratedCode);
        Assert.Empty(sut.CodeMessage);
        totp.Verify(value => value.GenerateAsync(accountId), Times.Once);
    }

    [Fact]
    public async Task SelectedAccount_WhenAutoGenerateEnabled_GeneratesAndCopiesCodeImmediately()
    {
        var accountId = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(accountId))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("654321", 24, 30)));
        var clipboard = SuccessfulClipboard();
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        sut.EnableAutomaticCodeGenerationOnSelection();

        sut.SelectedAccount = new AccountListItemViewModel(accountId, "Issuer", "account");
        await WaitUntilAsync(() => sut.GeneratedCode == "654321");

        Assert.Equal(24, sut.RemainingSeconds);
        totp.Verify(value => value.GenerateAsync(accountId), Times.Once);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "654321",
            TimeSpan.FromSeconds(24),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SelectedAccount_WhenChangedDuringGeneration_ShowsOnlyLatestAccountCode()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstResult = new TaskCompletionSource<Result<TotpGenerationResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(firstId)).Returns(firstResult.Task);
        totp.Setup(value => value.GenerateAsync(secondId))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("222222", 20, 30)));
        var clipboard = SuccessfulClipboard();
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        sut.EnableAutomaticCodeGenerationOnSelection();

        sut.SelectedAccount = new AccountListItemViewModel(firstId, "First", "account");
        sut.SelectedAccount = new AccountListItemViewModel(secondId, "Second", "account");
        firstResult.SetResult(Result.Ok(new TotpGenerationResult("111111", 25, 30)));
        await WaitUntilAsync(() => sut.GeneratedCode == "222222");

        Assert.DoesNotContain("111111", sut.GeneratedCode, StringComparison.Ordinal);
        totp.Verify(value => value.GenerateAsync(secondId), Times.Once);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "222222",
            TimeSpan.FromSeconds(20),
            It.IsAny<CancellationToken>()), Times.Once);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "111111",
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateCodeAsync_WhenServiceFails_DoesNotExposeFailureDetail()
    {
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Fail<TotpGenerationResult>("SYNTHETIC-SECRET-DETAIL"));
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization())
        {
            SelectedAccount = new AccountListItemViewModel(Guid.NewGuid(), "Issuer", "account")
        };

        await sut.GenerateCodeAsync();

        Assert.Empty(sut.GeneratedCode);
        Assert.DoesNotContain("SECRET", sut.CodeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_RemovesRowsSelectionSearchAndGeneratedCode()
    {
        var id = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(id, "Issuer", "SECRET", "account")]));
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        using var sut = new AccountListViewModel(
            manager.Object,
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];
        sut.SearchText = "Issuer";
        await sut.GenerateCodeAsync();

        sut.Clear();

        Assert.Empty(sut.Accounts);
        Assert.Null(sut.SelectedAccount);
        Assert.Empty(sut.SearchText);
        Assert.Empty(sut.GeneratedCode);
        Assert.Empty(sut.CodeMessage);
    }

    [Fact]
    public async Task ClearSensitiveOutput_PreservesRowsAndSelectionButRemovesCode()
    {
        var id = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(id, "Issuer", "SECRET", "account")]));
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        using var sut = new AccountListViewModel(
            manager.Object,
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];
        await sut.GenerateCodeAsync();

        sut.ClearSensitiveOutput();

        Assert.Single(sut.Accounts);
        Assert.NotNull(sut.SelectedAccount);
        Assert.All(sut.Accounts, account => Assert.False(account.HasCode));
        Assert.Empty(sut.GeneratedCode);
        Assert.Empty(sut.CodeMessage);
    }

    [Fact]
    public async Task CopyCodeAsync_UsesRemainingLifetimeForConditionalClear()
    {
        var id = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 18, 30)));
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.Setup(value => value.CopyAndScheduleClearAsync(
                "123456",
                TimeSpan.FromSeconds(18),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization())
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };
        await sut.GenerateCodeAsync();

        await sut.CopyCodeAsync();

        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromSeconds(18),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(
            "Copied. Conditional clipboard clear is scheduled in 18 seconds.",
            sut.CodeMessage);
    }

    [Fact]
    public async Task CultureChanged_RelocalizesVisibleClipboardStatusMessage()
    {
        var id = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 15, 30)));
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.Setup(value => value.CopyAndScheduleClearAsync(
                "123456",
                TimeSpan.FromSeconds(15),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("en");
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            localization)
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };
        await sut.GenerateCodeAsync();
        await sut.CopyCodeAsync();

        localization.ApplyCulture("de");

        Assert.Equal(
            "Kopiert. Die Zwischenablage wird in 15 Sekunden geleert, sofern der Code unverändert ist.",
            sut.CodeMessage);
    }

    [Fact]
    public async Task GenerateQrAsync_ProjectsAndClearsSecretBearingBitmap()
    {
        var id = Guid.NewGuid();
        var png = new byte[] { 137, 80, 78, 71 };
        var image = new Mock<IImage>();
        var lifetime = new Mock<IDisposable>();
        var imageFactory = new Mock<IAvaloniaQrImageFactory>();
        imageFactory.Setup(value => value.Create(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new AvaloniaQrImageHandle(image.Object, lifetime.Object));
        var previewDialogs = new Mock<IAvaloniaQrPreviewDialogService>();
        var qr = new Mock<IAccountQrCodeService>();
        qr.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(() => Result.Ok(SensitiveBuffer.CopyFrom(png)));
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(),
            qr.Object,
            imageFactory.Object,
            Mock.Of<IAvaloniaDialogService>(),
            Localization(),
            qrPreviewDialogs: previewDialogs.Object)
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };

        await sut.GenerateQrAsync();

        Assert.False(sut.HasQrImage);
        previewDialogs.Verify(value => value.ShowAsync(
            image.Object,
            "Issuer · account — QR code",
            384,
            It.IsAny<CancellationToken>()), Times.Once);
        previewDialogs.Verify(value => value.Close(), Times.AtLeastOnce);
        lifetime.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task GenerateContextQrAsync_UsesRightClickedAccountInsteadOfSelection()
    {
        var selectedId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var image = Mock.Of<IImage>();
        var imageFactory = new Mock<IAvaloniaQrImageFactory>();
        imageFactory.Setup(value => value.Create(It.IsAny<ReadOnlyMemory<byte>>()))
            .Returns(new AvaloniaQrImageHandle(image, Mock.Of<IDisposable>()));
        var previewDialogs = new Mock<IAvaloniaQrPreviewDialogService>();
        var qr = new Mock<IAccountQrCodeService>();
        qr.Setup(value => value.GenerateAsync(contextId))
            .ReturnsAsync(() => Result.Ok(SensitiveBuffer.CopyFrom([137, 80, 78, 71])));
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(),
            qr.Object,
            imageFactory.Object,
            Mock.Of<IAvaloniaDialogService>(),
            Localization(),
            qrPreviewDialogs: previewDialogs.Object)
        {
            SelectedAccount = new AccountListItemViewModel(selectedId, "Selected", "account"),
            ContextAccount = new AccountListItemViewModel(contextId, "Context", "account")
        };

        await sut.GenerateContextQrAsync();

        qr.Verify(value => value.GenerateAsync(contextId), Times.Once);
        qr.Verify(value => value.GenerateAsync(selectedId), Times.Never);
        previewDialogs.Verify(value => value.ShowAsync(
            image,
            "Context · account — QR code",
            384,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAccountAsync_CreatesNormalizedAccountAfterClearingBoundSecret()
    {
        var manager = new Mock<IAccountManager>();
        Account? created = null;
        manager.SetupSequence(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]))
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(
                created is null ? [] : [created]));
        AccountListViewModel? sut = null;
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account account) =>
            {
                Assert.Equal(string.Empty, sut!.EditorSecret);
                created = account;
                return Result.Ok();
            });
        sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "  GitHub  ";
        sut.EditorAccountName = " alice@example.test ";
        sut.EditorSecret = "JBSW Y3DP-EHPK3PXP";

        await sut.SaveAccountAsync();

        Assert.NotNull(created);
        Assert.Equal("GitHub", created.Issuer);
        Assert.Equal("alice@example.test", created.AccountName);
        Assert.Equal(ValidSecret, created.Secret);
        Assert.Equal(30, created.PeriodSeconds);
        Assert.False(sut.IsEditorVisible);
        Assert.Equal("Account saved.", sut.Message);
    }

    [Fact]
    public async Task SaveAccountAsync_WithCustomPeriod_PersistsPeriodAndProjectsItsBadge()
    {
        var manager = new Mock<IAccountManager>();
        Account? created = null;
        manager.SetupSequence(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]))
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(
                created is null ? [] : [created]));
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => created = account)
            .ReturnsAsync(Result.Ok());
        var sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "Example";
        sut.EditorSecret = ValidSecret;
        sut.EditorPeriodSeconds = 60;

        await sut.SaveAccountAsync();

        Assert.NotNull(created);
        Assert.Equal(60, created.PeriodSeconds);
        var row = Assert.Single(sut.Accounts);
        Assert.True(row.HasCustomPeriod);
        Assert.Equal("60 s", row.CustomPeriodLabel);
    }

    [Fact]
    public async Task SaveAccountAsync_WithUnsupportedPeriod_DoesNotWrite()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "Example";
        sut.EditorSecret = ValidSecret;
        sut.EditorPeriodSeconds = 3601;

        await sut.SaveAccountAsync();

        manager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal("Enter a code period between 5 and 3600 seconds.", sut.EditorPeriodMessage);
        Assert.True(sut.IsAdvancedOptionsExpanded);
        Assert.Equal(string.Empty, sut.EditorSecret);
    }

    [Fact]
    public async Task SaveAccountAsync_WithEmptyPeriod_ShowsFieldErrorWithoutWriting()
    {
        var manager = new Mock<IAccountManager>();
        var sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "Example";
        sut.EditorSecret = ValidSecret;
        sut.EditorPeriodSeconds = null;

        await sut.SaveAccountAsync();

        manager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal("Enter a code period between 5 and 3600 seconds.", sut.EditorPeriodMessage);
        Assert.Equal(string.Empty, sut.EditorIssuerMessage);
        Assert.Equal(string.Empty, sut.EditorSecretMessage);
    }

    [Fact]
    public async Task AccountEditor_ValidationMessagesAreFieldSpecificAndResetWhenReopened()
    {
        var sut = CreateSut(Mock.Of<IAccountManager>());
        await sut.BeginAddAsync();

        await sut.SaveAccountAsync();
        Assert.Equal("Issuer is required.", sut.EditorIssuerMessage);
        Assert.Equal(string.Empty, sut.EditorSecretMessage);

        sut.EditorIssuer = "Example";
        await sut.SaveAccountAsync();
        Assert.Equal(string.Empty, sut.EditorIssuerMessage);
        Assert.Equal("Enter a valid Base32 secret.", sut.EditorSecretMessage);

        sut.EditorPeriodSeconds = null;
        await sut.CancelEditAsync();
        await sut.BeginAddAsync();

        Assert.Equal(30, sut.EditorPeriodSeconds);
        Assert.Equal(string.Empty, sut.EditorIssuerMessage);
        Assert.Equal(string.Empty, sut.EditorSecretMessage);
        Assert.Equal(string.Empty, sut.EditorPeriodMessage);
        Assert.Equal(string.Empty, sut.EditorMessage);
    }

    [Fact]
    public async Task EditAccount_EmptyPeriodErrorIsResetWhenEditorIsReopened()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "alice");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        sut.SelectedAccount = Assert.Single(sut.Accounts);
        await sut.BeginEditAsync();
        sut.EditorPeriodSeconds = null;

        await sut.SaveAccountAsync();

        manager.Verify(value => value.UpdateAsync(
            It.IsAny<Account>(),
            It.IsAny<Account>()), Times.Never);
        Assert.Equal("Enter a code period between 5 and 3600 seconds.", sut.EditorPeriodMessage);

        await sut.CancelEditAsync();
        await sut.BeginEditAsync();

        Assert.Equal(30, sut.EditorPeriodSeconds);
        Assert.Equal(string.Empty, sut.EditorPeriodMessage);
        Assert.Equal(string.Empty, sut.EditorMessage);
    }

    [Fact]
    public async Task AdvancedOptions_AfterClosingEditor_StartsCollapsedForNextAction()
    {
        var account = new Account(Guid.NewGuid(), "Example", ValidSecret, "alice");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        var sut = CreateSut(manager.Object);

        await sut.BeginAddAsync();
        Assert.False(sut.IsAdvancedOptionsExpanded);
        sut.IsAdvancedOptionsExpanded = true;
        await sut.CancelEditAsync();

        await sut.LoadAsync();
        sut.SelectedAccount = Assert.Single(sut.Accounts);
        await sut.BeginEditAsync();

        Assert.False(sut.IsAdvancedOptionsExpanded);
    }

    [Fact]
    public async Task SaveAccountAsync_RejectsDuplicateIssuerAndAccountWithoutWriting()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice@example.test")]));
        var sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "github";
        sut.EditorAccountName = "ALICE@example.test";
        sut.EditorSecret = ValidSecret;

        await sut.SaveAccountAsync();

        manager.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        Assert.Equal(
            "An account with the same issuer and account name already exists.",
            sut.EditorMessage);
        Assert.Equal(string.Empty, sut.EditorSecret);
    }

    [Fact]
    public async Task BeginEditAndSave_UsesSelectedIdentityAndClearsSecretAtBoundaries()
    {
        var id = Guid.NewGuid();
        var original = new Account(id, "GitHub", ValidSecret, "alice@example.test");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([original]));
        AccountListViewModel? sut = null;
        manager.Setup(value => value.UpdateAsync(original, It.IsAny<Account>()))
            .ReturnsAsync((Account _, Account updated) =>
            {
                Assert.Equal(string.Empty, sut!.EditorSecret);
                Assert.Equal(id, updated.ID);
                return Result.Ok();
            });
        sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];

        await sut.BeginEditAsync();
        Assert.Equal(ValidSecret, sut.EditorSecret);
        sut.EditorIssuer = "GitHub Enterprise";
        await sut.SaveAccountAsync();

        manager.Verify(value => value.UpdateAsync(
            original,
            It.Is<Account>(account => account.Issuer == "GitHub Enterprise")), Times.Once);
        Assert.False(sut.IsEditorVisible);
        Assert.Equal(string.Empty, sut.EditorSecret);
    }

    [Fact]
    public async Task BeginContextEditAsync_TargetsRightClickedRowWithoutChangingSelection()
    {
        var first = new Account(Guid.NewGuid(), "First", ValidSecret, "selected");
        var second = new Account(Guid.NewGuid(), "Second", ValidSecret, "context");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([first, second]));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];
        sut.ContextAccount = sut.Accounts[1];

        await sut.BeginContextEditAsync();

        Assert.Equal(first.ID, sut.SelectedAccount!.Id);
        Assert.Equal("Second", sut.EditorIssuer);
        Assert.Equal("context", sut.EditorAccountName);
        Assert.True(sut.IsEditorVisible);
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesOnlyAfterOwnedConfirmation()
    {
        var id = Guid.NewGuid();
        var account = new Account(id, "GitHub", ValidSecret, "alice@example.test");
        var manager = new Mock<IAccountManager>();
        manager.SetupSequence(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]))
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]))
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        manager.Setup(value => value.DeleteAsync(account)).ReturnsAsync(Result.Ok());
        var dialogs = new Mock<IAvaloniaDialogService>();
        ConfirmationDialogRequest? deleteConfirmation = null;
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ConfirmationDialogRequest, CancellationToken>(
                (request, _) => deleteConfirmation = request)
            .ReturnsAsync(true);
        var sut = CreateSut(
            manager.Object,
            dialogs.Object,
            TimeSpan.FromMilliseconds(100));
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];

        await sut.DeleteAccountAsync();

        manager.Verify(value => value.DeleteAsync(account), Times.Once);
        Assert.Empty(sut.Accounts);
        Assert.NotNull(deleteConfirmation);
        Assert.Equal(
            [
                "GitHub (alice@example.test)",
                "The encrypted account entry will be permanently removed.",
                "This cannot be undone."
            ],
            deleteConfirmation.Message.Split('\n'));
        Assert.Equal("Account deleted.", sut.Message);
        await WaitUntilAsync(() => !sut.HasMessage);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task DeleteContextAccountAsync_DeletesRightClickedRowNotSelection()
    {
        var first = new Account(Guid.NewGuid(), "First", ValidSecret, "selected");
        var second = new Account(Guid.NewGuid(), "Second", ValidSecret, "context");
        var remaining = new List<Account> { first, second };
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(remaining.ToArray()));
        manager.Setup(value => value.DeleteAsync(second))
            .Callback(() => remaining.Remove(second))
            .ReturnsAsync(Result.Ok());
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var sut = CreateSut(manager.Object, dialogs.Object);
        await sut.LoadAsync();
        sut.SelectedAccount = sut.Accounts[0];
        sut.ContextAccount = sut.Accounts[1];

        await sut.DeleteContextAccountAsync();

        manager.Verify(value => value.DeleteAsync(second), Times.Once);
        manager.Verify(value => value.DeleteAsync(first), Times.Never);
        Assert.Single(sut.Accounts);
        Assert.Equal(first.ID, sut.Accounts[0].Id);
    }

    [Fact]
    public async Task ClearSensitiveOutput_DiscardsUncommittedEditorSecret()
    {
        var sut = CreateSut(Mock.Of<IAccountManager>());
        await sut.BeginAddAsync();
        sut.EditorSecret = ValidSecret;

        sut.ClearSensitiveOutput();

        Assert.False(sut.IsEditorVisible);
        Assert.Equal(string.Empty, sut.EditorSecret);
    }

    [Fact]
    public async Task CountdownAsync_RefreshesCodeAtPeriodBoundary()
    {
        var id = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("111111", 1, 30)));
        totp.Setup(value => value.GenerateManyAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == id)))
            .ReturnsAsync(Result.Ok(new AccountTotpGenerationBatch(
                new Dictionary<Guid, TotpGenerationResult>
                {
                    [id] = new("222222", 30, 30)
                },
                new HashSet<Guid>())));
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization(),
            TimeSpan.FromMilliseconds(10))
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };

        await sut.GenerateCodeAsync();
        await WaitUntilAsync(() => sut.GeneratedCode == "222222");

        Assert.InRange(sut.RemainingSeconds, 1, 30);
        Assert.Equal(30, sut.PeriodSeconds);
        Assert.Empty(sut.CodeMessage);
        totp.Verify(value => value.GenerateAsync(id), Times.Once);
        totp.Verify(value => value.GenerateManyAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == id)), Times.Once);
    }

    [Fact]
    public async Task CopyCodeAsync_WhenAutomaticClearIsDisabled_CopiesWithoutSchedulingClear()
    {
        var id = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.Setup(value => value.CopyAsync(
                "123456",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings
        {
            ClearClipboardEnabled = false
        });
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization(),
            settingsService: settings.Object)
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };
        await sut.GenerateCodeAsync();

        await sut.CopyCodeAsync();

        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        clipboard.Verify(value => value.CopyAsync(
            "123456", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Copied.", sut.CodeMessage);
    }

    [Fact]
    public async Task CopyCodeAsync_WhenConditionalClearIsUnavailable_CopiesAndShowsLocalizedWarning()
    {
        var id = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.SetupGet(value => value.Capabilities).Returns(ClipboardCapabilities.WriteText);
        clipboard.Setup(value => value.CopyAndScheduleClearAsync(
                "123456",
                TimeSpan.FromSeconds(15),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("Conditional clear unavailable."));
        clipboard.Setup(value => value.CopyAsync(
                "123456",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("de");
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(new AppSettings
        {
            ClearClipboardEnabled = true,
            ClearClipboardSeconds = 15
        });
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            localization,
            settingsService: settings.Object)
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };
        await sut.GenerateCodeAsync();

        await sut.CopyCodeAsync();

        clipboard.Verify(value => value.CopyAsync(
            "123456", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(
            "Kopiert. Das automatische Leeren der Zwischenablage ist auf dieser Plattform nicht verfügbar.",
            sut.CodeMessage);
    }

    private static AccountListViewModel CreateSut(
        IAccountManager manager,
        IAvaloniaDialogService? dialogs = null,
        TimeSpan? transientMessageDuration = null) =>
        new(
            manager,
            Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            dialogs ?? Mock.Of<IAvaloniaDialogService>(),
            Localization(),
            transientMessageDuration: transientMessageDuration);

    private static Mock<IAsyncClipboardService> SuccessfulClipboard()
    {
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.Setup(value => value.CopyAndScheduleClearAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        return clipboard;
    }

    private static IAvaloniaLocalizationService Localization()
    {
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("en");
        return localization;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }
}
