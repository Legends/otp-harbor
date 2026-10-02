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
using TOTP.Avalonia.Shared.Branding;

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
        Assert.False(sut.HasAnyAccounts);
        Assert.False(sut.HasNoSearchResults);
        Assert.False(sut.HasAccountNavigationCards);
        Assert.False(sut.HasMessage);
    }

    [Fact]
    public async Task RevealImportedAccountAsync_RequestsRevealHighlightsAndAnnouncesImportedRow()
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
        AccountListItemViewModel? revealRequest = null;
        sut.AccountRevealRequested += account => revealRequest = account;

        await sut.RevealImportedAccountAsync(
            importedId,
            highlightAsNew: true,
            AvaloniaStringKeys.QrAccountAdded);

        Assert.Null(sut.SelectedAccount);
        Assert.Equal(importedId, revealRequest?.Id);
        Assert.True(revealRequest?.IsRecentlyAdded);
        Assert.Equal(AvaloniaStringKeys.QrAccountAdded, sut.Message);

        sut.SearchText = "does-not-match";
        sut.SearchText = string.Empty;

        Assert.Null(sut.SelectedAccount);
        Assert.False(sut.Accounts.Single(account => account.Id == importedId).IsRecentlyAdded);
    }

    [Fact]
    public async Task RevealImportedAccountAsync_LeavesFavoritesFilterToShowNonFavoriteImport()
    {
        var imported = new Account(Guid.NewGuid(), "Imported", ValidSecret, "alice");
        var favorite = new Account(
            Guid.NewGuid(),
            "Favorite",
            ValidSecret,
            "bob",
            isFavorite: true);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([favorite, imported]));
        using var sut = CreateSut(manager.Object);
        AccountListItemViewModel? revealRequest = null;
        sut.AccountRevealRequested += account => revealRequest = account;
        await sut.LoadAsync();
        sut.ToggleFavoritesFilterCommand.Execute(null);
        Assert.True(sut.IsFavoritesFilterSelected);

        await sut.RevealImportedAccountAsync(imported.ID, true, "Imported safely.");

        Assert.False(sut.IsFavoritesFilterSelected);
        Assert.Null(sut.SelectedAccount);
        Assert.Equal(imported.ID, revealRequest?.Id);
        Assert.True(revealRequest?.IsRecentlyAdded);
    }

    [Fact]
    public async Task SearchText_WithNoMatches_ExposesFilteredEmptyState()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", isFavorite: true)
            ]));
        var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        Assert.True(sut.ShouldShowAccountNavigationCards);

        sut.SearchText = "missing";

        Assert.False(sut.HasNoAccounts);
        Assert.True(sut.HasAnyAccounts);
        Assert.True(sut.HasNoSearchResults);
        Assert.False(sut.ShouldShowAccountNavigationCards);
    }

    [Fact]
    public async Task SelectedSortOption_ReordersAccountsAndAppliesAfterFiltering()
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(Guid.NewGuid(), "Zeta", ValidSecret, "alice"),
                new(Guid.NewGuid(), "Alpha", ValidSecret, "zoe"),
                new(Guid.NewGuid(), "Beta", ValidSecret, "alice")
            ]));
        using var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        Assert.True(sut.HasMultipleAccounts);
        Assert.Equal(4, sut.SortOptions.Count);
        Assert.Equal(AccountSortMode.Issuer, sut.SelectedSortOption?.Mode);
        Assert.Equal(["Alpha", "Beta", "Zeta"], sut.Accounts.Select(account => account.Issuer));

        sut.SelectedSortOption = sut.SortOptions.Single(
            option => option.Mode == AccountSortMode.AccountName);

        Assert.Equal(["alice", "alice", "zoe"], sut.Accounts.Select(account => account.AccountName));
        Assert.Equal(["Beta", "Zeta", "Alpha"], sut.Accounts.Select(account => account.Issuer));

        sut.SearchText = "alice";

        Assert.Equal(["Beta", "Zeta"], sut.Accounts.Select(account => account.Issuer));

        await sut.SelectIssuerDescendingSortAsync();

        Assert.Equal(["Zeta", "Beta"], sut.Accounts.Select(account => account.Issuer));
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
            .Reverse()
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

#if DEBUG
    [Fact]
    public async Task DebugSyntheticAccounts_UseOneBatchCommitAndPreserveRegularAccounts()
    {
        var regular = new Account(Guid.NewGuid(), "Existing", ValidSecret, "regular");
        IReadOnlyList<Account> stored = [regular];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(stored));
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(accounts => stored = accounts.ToArray())
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);

        Assert.True(await sut.AddDebugSyntheticAccountsAsync(600));

        Assert.Equal(601, stored.Count);
        Assert.Contains(stored, account => account.ID == regular.ID);
        Assert.Equal(600, stored.Count(account =>
            account.AccountName?.StartsWith(
                "otp-harbor-debug-load-test:",
                StringComparison.Ordinal) == true));
        Assert.Equal(6, stored.Where(account => account.Group is not null)
            .Select(account => account.Group!.Id)
            .Distinct()
            .Count());
        manager.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
    }
#endif

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
        Assert.Equal("Copied", sut.Accounts[1].CopyConfirmation);
        Assert.Empty(sut.Accounts[0].CopyConfirmation);
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
        Assert.Null(sut.SelectedAccount);
        Assert.Equal("Showing 2 of 3 accounts", sut.SearchResultSummary);

        sut.SearchText = "  bob  ";
        Assert.Single(sut.Accounts);
        Assert.Equal("Microsoft", sut.Accounts[0].Issuer);
        Assert.Null(sut.SelectedAccount);
        Assert.Equal("Showing 1 of 3 accounts", sut.SearchResultSummary);

        sut.SearchText = string.Empty;
        Assert.Equal(3, sut.Accounts.Count);
    }

    [Fact]
    public async Task GroupFilter_HidesGroupedAccountsAndSearchesGloballyByGroupOrAccount()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice@example.test", group: group),
            new(Guid.NewGuid(), "github", ValidSecret, "bob@example.test", group: group),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob@example.test")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        var work = Assert.Single(sut.Groups);
        Assert.Equal("Work", work.Name);
        Assert.Equal(2, work.AccountCount);
        Assert.True(sut.HasAccountNavigationCards);
        Assert.Equal(1, sut.UngroupedCount);
        Assert.True(sut.HasUngroupedAccounts);
        Assert.False(sut.IsUngroupedFilterSelected);
        Assert.Equal(3, sut.Accounts.Count);

        sut.SearchText = "work";
        Assert.Equal(2, sut.Accounts.Count);
        Assert.All(sut.Accounts, account => Assert.Equal(group.Id, account.Group?.Id));
        sut.SearchText = string.Empty;

        work.SelectCommand.Execute(null);

        Assert.True(sut.HasSelectedGroup);
        Assert.False(sut.IsUngroupedFilterSelected);
        Assert.True(sut.HasActiveAccountFilter);
        Assert.Equal(2, sut.Accounts.Count);

        sut.SearchText = "bob";

        Assert.False(sut.HasSelectedGroup);
        Assert.Single(sut.Groups);
        Assert.Equal(2, sut.Accounts.Count);
        Assert.Contains(sut.Accounts, account => account.Issuer == "github");
        Assert.Contains(sut.Accounts, account => account.Issuer == "Microsoft");
        Assert.Equal("Showing 2 of 3 accounts", sut.SearchResultSummary);

        sut.ClearGroupFilterCommand.Execute(null);
        Assert.Equal(2, sut.Accounts.Count);
    }

    [Fact]
    public async Task ClearGroupFilter_ReturnsToAllAccounts()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", group: group, isFavorite: true),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        sut.Groups.Single().SelectCommand.Execute(null);
        Assert.True(sut.HasSelectedGroup);
        Assert.Equal("GitHub", Assert.Single(sut.Accounts).Issuer);

        sut.ClearGroupFilterCommand.Execute(null);

        Assert.False(sut.IsFavoritesFilterSelected);
        Assert.False(sut.HasSelectedGroup);
        Assert.False(sut.IsUngroupedFilterSelected);
        Assert.Equal(2, sut.Accounts.Count);
    }

    [Fact]
    public async Task DefaultView_ShowsGroupedAndUngroupedAccounts()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", group: group),
            new(Guid.NewGuid(), "GitLab", ValidSecret, "bob", group: group),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "carol")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        Assert.Equal(3, sut.AllAccountCount);
        Assert.Equal(3, sut.Accounts.Count);
        Assert.False(sut.IsUngroupedFilterSelected);
        Assert.False(sut.IsAllAccountsFilterSelected);
        Assert.False(sut.HasSelectedAccountNavigationCard);
        Assert.Contains(sut.Accounts, account => account.Issuer == "Microsoft");
    }

    [Theory]
    [InlineData("en", "All accounts")]
    [InlineData("de", "Alle Konten")]
    [InlineData("fr", "Tous les comptes")]
    [InlineData("es", "Todas las cuentas")]
    public async Task AccountNavigationBackLabel_AlwaysReturnsToAllAccounts(
        string cultureName,
        string expected)
    {
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", isFavorite: true),
                new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob")
            ]));
        using var sut = CreateSut(manager.Object, localization: Localization(cultureName));
        await sut.LoadAsync();
        sut.ToggleFavoritesFilterCommand.Execute(null);

        Assert.Equal(expected, sut.AccountNavigationBackLabel);

        sut.SearchText = "git";

        Assert.Equal(expected, sut.AccountNavigationBackLabel);
    }

    [Fact]
    public async Task FavoritesFilter_IncludesGroupedAccountsAndCanBeNarrowedBySearch()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", group: group, isFavorite: true),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob", isFavorite: true),
            new(Guid.NewGuid(), "Example", ValidSecret, "carol")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        Assert.True(sut.HasFavoriteAccounts);
        Assert.True(sut.HasAccountNavigationCards);
        Assert.Equal(2, sut.FavoriteCount);
        Assert.Equal(3, sut.Accounts.Count);
        sut.ToggleFavoritesFilterCommand.Execute(null);

        Assert.True(sut.IsFavoritesFilterSelected);
        Assert.True(sut.HasSelectedAccountNavigationCard);
        Assert.True(sut.ClearGroupFilterCommand.CanExecute(null));
        Assert.True(sut.HasActiveAccountFilter);
        Assert.Equal(2, sut.Accounts.Count);
        Assert.All(sut.Accounts, account => Assert.True(account.IsFavorite));
        sut.ContextAccount = sut.Accounts[0];
        Assert.True(sut.BeginContextEditCommand.CanExecute(null));
        Assert.True(sut.DeleteContextAccountCommand.CanExecute(null));

        sut.SearchText = "git";

        var result = Assert.Single(sut.Accounts);
        Assert.Equal("GitHub", result.Issuer);
        Assert.True(sut.IsFavoritesFilterSelected);

        sut.ClearGroupFilterCommand.Execute(null);

        Assert.False(sut.IsFavoritesFilterSelected);
        Assert.False(sut.HasSelectedAccountNavigationCard);
        Assert.False(sut.ClearGroupFilterCommand.CanExecute(null));

        sut.ToggleFavoritesFilterCommand.Execute(null);

        sut.Groups.Single().SelectCommand.Execute(null);

        Assert.False(sut.IsFavoritesFilterSelected);
        Assert.True(sut.HasSelectedGroup);
        Assert.Single(sut.Accounts);
    }

    [Fact]
    public async Task ToggleAccountFavoriteAsync_AddsAndRemovesFavoriteFromVisibleRow()
    {
        var stored = new List<Account>
        {
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice")
        };
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(stored));
        manager.Setup(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()))
            .Callback<Account, Account>((_, updated) => stored[0] = updated)
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        var row = Assert.Single(sut.Accounts);

        Assert.NotNull(row.ToggleFavoriteCommand);
        await sut.ToggleAccountFavoriteAsync(row);

        Assert.True(stored[0].IsFavorite);
        Assert.Same(row, Assert.Single(sut.Accounts));
        Assert.True(row.IsFavorite);
        Assert.False(sut.HasMessage);

        sut.ToggleFavoritesFilterCommand.Execute(null);
        Assert.True(sut.IsFavoritesFilterSelected);
        await sut.ToggleAccountFavoriteAsync(row);

        Assert.False(stored[0].IsFavorite);
        Assert.False(sut.IsFavoritesFilterSelected);
        Assert.False(Assert.Single(sut.Accounts).IsFavorite);
        Assert.False(sut.HasMessage);
        manager.Verify(value => value.UpdateAsync(
            It.IsAny<Account>(),
            It.IsAny<Account>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ToggleAccountFavoriteAsync_RemovingOneFavoriteKeepsFavoritesCardEnabledAfterExit()
    {
        var stored = new List<Account>
        {
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", isFavorite: true),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob", isFavorite: true)
        };
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(stored));
        manager.Setup(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()))
            .Callback<Account, Account>((_, updated) =>
            {
                var index = stored.FindIndex(account => account.ID == updated.ID);
                stored[index] = updated;
            })
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        sut.ToggleFavoritesFilterCommand.Execute(null);
        var row = sut.Accounts[0];
        var starCommandStateChanges = 0;
        var favoriteGroupCommandStateChanges = 0;
        var addGroupCommandStateChanges = 0;
        var busyStateChanges = 0;
        row.ToggleFavoriteCommand!.CanExecuteChanged += (_, _) => starCommandStateChanges++;
        sut.ToggleFavoritesFilterCommand.CanExecuteChanged +=
            (_, _) => favoriteGroupCommandStateChanges++;
        sut.BeginAddGroupCommand.CanExecuteChanged += (_, _) => addGroupCommandStateChanges++;
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AccountListViewModel.IsBusy)) busyStateChanges++;
        };

        await sut.ToggleAccountFavoriteAsync(row);
        Assert.Equal(0, starCommandStateChanges);
        Assert.Equal(0, favoriteGroupCommandStateChanges);
        Assert.Equal(0, addGroupCommandStateChanges);
        Assert.Equal(0, busyStateChanges);

        sut.ClearGroupFilterCommand.Execute(null);

        Assert.Equal(1, sut.FavoriteCount);
        Assert.False(sut.IsFavoritesFilterSelected);
        Assert.True(sut.ToggleFavoritesFilterCommand.CanExecute(null));
        manager.Verify(
            value => value.GetAllOtpEntriesSortedAsync(),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ToggleAccountFavoriteAsync_WhenSaveFails_KeepsCurrentStateAndReportsError()
    {
        var account = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        manager.Setup(value => value.UpdateAsync(account, It.IsAny<Account>()))
            .ReturnsAsync(Result.Fail("synthetic failure"));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        var row = Assert.Single(sut.Accounts);

        await sut.ToggleAccountFavoriteAsync(row);

        Assert.False(row.IsFavorite);
        Assert.Equal("The favorite setting could not be saved.", sut.Message);
        Assert.Equal(NotificationSeverity.Error, sut.Notification.Severity);
    }

    [Fact]
    public async Task LoadAsync_WithStoredLegacyGroupColor_RestoresVisibleGroupAndColor()
    {
        var storedGroup = new AccountGroup(Guid.NewGuid(), "Personal", "#7c3aed");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice", group: storedGroup)
            ]));
        using var sut = CreateSut(manager.Object);

        await sut.LoadAsync();

        var group = Assert.Single(sut.Groups);
        Assert.Equal("#7C3AED", group.Group.Color);
        Assert.Equal(Color.Parse("#7C3AED"), Assert.IsType<SolidColorBrush>(group.Foreground).Color);
    }

    [Fact]
    public async Task SaveGroup_CreatesThenEditsColorNameAndAssignments()
    {
        var first = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var second = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "bob");
        var third = new Account(Guid.NewGuid(), "Google", ValidSecret, "carol");
        IReadOnlyList<Account> accounts = [first, second, third];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(accounts));
        manager.Setup(value => value.SaveGroupAsync(
                It.IsAny<AccountGroup>(),
                It.IsAny<IReadOnlyCollection<Guid>>()))
            .Callback<AccountGroup, IReadOnlyCollection<Guid>>((group, selectedIds) =>
            {
                var ids = selectedIds.ToHashSet();
                accounts = accounts.Select(account =>
                    ids.Contains(account.ID)
                        ? account.WithGroup(group)
                        : account.Group?.Id == group.Id
                            ? account.WithGroup(null)
                            : account).ToArray();
            })
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        await sut.BeginAddGroupAsync();
        sut.GroupEditorName = "Work";
        sut.GroupEditorAccounts[0].IsSelected = true;
        sut.GroupEditorAccounts[1].IsSelected = true;
        sut.SelectedGroupColor = sut.GroupColorOptions.Single(color => color.Hex == "#4F6BED");
        await sut.SaveGroupAsync();

        var created = Assert.Single(sut.Groups);
        Assert.Equal("Work", created.Name);
        Assert.Equal(2, created.AccountCount);
        created.EditCommand.Execute(null);
        Assert.True(sut.IsEditingExistingGroup);
        sut.GroupEditorName = "Office";
        sut.GroupEditorAccounts[0].IsSelected = false;
        sut.GroupEditorAccounts[2].IsSelected = true;
        sut.SelectedGroupColor = sut.GroupColorOptions.Single(color => color.Hex == "#E45757");
        await sut.SaveGroupAsync();

        var edited = Assert.Single(sut.Groups);
        Assert.Equal("Office", edited.Name);
        Assert.Equal("#E45757", edited.Group.Color);
        var editedBackground = Assert.IsType<SolidColorBrush>(edited.Background);
        Assert.Equal(Color.Parse("#E45757").R, editedBackground.Color.R);
        Assert.Equal(Color.Parse("#E45757").G, editedBackground.Color.G);
        Assert.Equal(Color.Parse("#E45757").B, editedBackground.Color.B);
        Assert.Equal(2, edited.AccountCount);
        Assert.DoesNotContain(accounts, account => account.ID == first.ID && account.Group is not null);
        Assert.Contains(accounts, account => account.ID == third.ID && account.Group?.Id == edited.Id);
    }

    [Fact]
    public async Task ResumeState_ExistingGroupEditorReloadsPersistedGroupById()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Persisted group", "#4F6BED");
        var account = new Account(
            Guid.NewGuid(),
            "Example",
            ValidSecret,
            "user",
            group: group);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        sut.Groups.Single().EditCommand.Execute(null);
        sut.GroupEditorName = "Unsaved group name";

        var resumeState = sut.CaptureResumeState();
        sut.Clear();
        await sut.LoadAsync();
        await sut.RestoreResumeStateAsync(resumeState);

        Assert.True(sut.IsGroupEditorVisible);
        Assert.True(sut.IsEditingExistingGroup);
        Assert.Equal("Persisted group", sut.GroupEditorName);
    }

    [Fact]
    public async Task EditFavorites_ChangesColorAndAssignmentsThroughBuiltInGroupEditor()
    {
        var first = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice", isFavorite: true);
        var second = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "bob");
        IReadOnlyList<Account> accounts = [first, second];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(accounts));
        manager.Setup(value => value.SaveFavoritesAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Callback<IReadOnlyCollection<Guid>>(selectedIds =>
            {
                var ids = selectedIds.ToHashSet();
                accounts = accounts.Select(account => account.WithFavorite(ids.Contains(account.ID))).ToArray();
            })
            .ReturnsAsync(Result.Ok());
        var currentSettings = new AppSettings { FavoriteGroupColor = "#F59E0B" };
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(currentSettings);
        settings.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object, settingsService: settings.Object);
        await sut.LoadAsync();

        await sut.BeginEditFavoritesAsync();

        Assert.True(sut.IsEditingFavorites);
        Assert.False(sut.IsEditingExistingGroup);
        Assert.False(sut.IsCreatingGroup);
        Assert.True(sut.GroupEditorAccounts.Single(item => item.AccountId == first.ID).IsSelected);
        Assert.False(sut.GroupEditorAccounts.Single(item => item.AccountId == second.ID).IsSelected);
        sut.GroupEditorAccounts.Single(item => item.AccountId == first.ID).IsSelected = false;
        sut.GroupEditorAccounts.Single(item => item.AccountId == second.ID).IsSelected = true;
        sut.SelectedGroupColor = sut.GroupColorOptions.Single(color => color.Hex == "#E45757");
        await sut.SaveGroupAsync();

        Assert.False(accounts.Single(account => account.ID == first.ID).IsFavorite);
        Assert.True(accounts.Single(account => account.ID == second.ID).IsFavorite);
        Assert.Equal("#E45757", currentSettings.FavoriteGroupColor);
        Assert.Equal("#E45757", sut.FavoriteGroupColor);
        Assert.Equal("Favorites saved.", sut.Message);
        Assert.False(sut.IsGroupEditorVisible);
        settings.Verify(value => value.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task EmptyFavorites_RemainsVisibleAndEditableSoAccountsCanBeAdded()
    {
        var first = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var second = new Account(Guid.NewGuid(), "Microsoft", ValidSecret, "bob");
        IReadOnlyList<Account> accounts = [first, second];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(accounts));
        manager.Setup(value => value.SaveFavoritesAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Callback<IReadOnlyCollection<Guid>>(selectedIds =>
            {
                var ids = selectedIds.ToHashSet();
                accounts = accounts.Select(account => account.WithFavorite(ids.Contains(account.ID))).ToArray();
            })
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        Assert.False(sut.HasAccountNavigationCards);
        Assert.False(sut.HasFavoriteAccounts);
        Assert.Equal(0, sut.FavoriteCount);
        Assert.True(sut.BeginEditFavoritesCommand.CanExecute(null));
        Assert.True(sut.ToggleFavoritesFilterCommand.CanExecute(null));

        await sut.BeginEditFavoritesAsync();
        Assert.True(sut.IsEditingFavorites);
        Assert.All(sut.GroupEditorAccounts, account => Assert.False(account.IsSelected));
        sut.GroupEditorAccounts.Single(account => account.AccountId == second.ID).IsSelected = true;
        await sut.SaveGroupAsync();

        Assert.True(accounts.Single(account => account.ID == second.ID).IsFavorite);
        Assert.True(sut.HasFavoriteAccounts);
        Assert.Equal(1, sut.FavoriteCount);
        Assert.True(sut.IsFavoritesFilterSelected);
        Assert.Equal(second.ID, Assert.Single(sut.Accounts).Id);
    }

    [Fact]
    public async Task GroupEditorSearch_FiltersAccountsWithoutLosingHiddenSelectionsOrIcons()
    {
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice"),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        await sut.BeginAddGroupAsync();
        sut.GroupEditorAccounts[0].IsSelected = true;

        sut.GroupEditorSearchText = "micro bob";

        var visible = Assert.Single(sut.GroupEditorAccounts);
        Assert.Equal("Microsoft", visible.Issuer);
        Assert.NotNull(visible.Brand);

        sut.GroupEditorSearchText = string.Empty;

        Assert.Equal(2, sut.GroupEditorAccounts.Count);
        Assert.True(sut.GroupEditorAccounts.Single(account => account.Issuer == "GitHub").IsSelected);
    }

    [Fact]
    public async Task SaveNewAccount_WhileViewingGroup_AssignsItToThatGroup()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var existing = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice", group: group);
        Account? added = null;
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(
                added is null ? [existing] : [existing, added]));
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => added = account)
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        Assert.Single(sut.Groups).SelectCommand.Execute(null);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "Microsoft";
        sut.EditorAccountName = "bob";
        sut.EditorSecret = ValidSecret;

        await sut.SaveAccountAsync();

        Assert.NotNull(added);
        Assert.Equal(group, added.Group);
        Assert.Equal(2, sut.Accounts.Count);
        Assert.Equal(added.ID, sut.SelectedAccount?.Id);
    }

    [Fact]
    public async Task DeleteGroup_ConfirmsThenKeepsAccountsUngrouped()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice", group: group),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob", group: group)
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok(accounts));
        manager.Setup(value => value.DeleteGroupAsync(group.Id))
            .Callback(() => accounts = accounts.Select(account => account.WithGroup(null)).ToArray())
            .ReturnsAsync(Result.Ok());
        var dialogs = new Mock<IAvaloniaDialogService>();
        dialogs.Setup(value => value.ConfirmAsync(
                It.IsAny<ConfirmationDialogRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var sut = CreateSut(manager.Object, dialogs.Object);
        await sut.LoadAsync();
        var work = Assert.Single(sut.Groups);

        work.DeleteCommand.Execute(null);
        await Task.Yield();

        Assert.Empty(sut.Groups);
        Assert.Equal(2, sut.Accounts.Count);
        Assert.All(accounts, account => Assert.Null(account.Group));
    }

    [Fact]
    public async Task SearchText_WithMultipleTerms_RequiresEveryTermAcrossIssuerAndAccountName()
    {
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice@example.test"),
            new(Guid.NewGuid(), "GitHub", ValidSecret, "bob@example.test"),
            new(Guid.NewGuid(), "Example", ValidSecret, "alice@example.test")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        sut.SearchText = "  github\t alice  ";

        var match = Assert.Single(sut.Accounts);
        Assert.Equal("GitHub", match.Issuer);
        Assert.Equal("alice@example.test", match.AccountName);
    }

    [Fact]
    public async Task SearchText_WithOnlyWhitespace_DoesNotActivateFiltering()
    {
        IReadOnlyList<Account> accounts =
        [
            new(Guid.NewGuid(), "GitHub", ValidSecret, "alice@example.test"),
            new(Guid.NewGuid(), "Microsoft", ValidSecret, "bob@example.test")
        ];
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(accounts));
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();

        sut.SearchText = " \t ";

        Assert.False(sut.HasSearchText);
        Assert.False(sut.HasActiveAccountFilter);
        Assert.Equal(2, sut.Accounts.Count);
    }

    [Fact]
    public async Task SearchText_WhenSelectedAccountIsFilteredOut_ClearsSelectionAndPreviousCode()
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
        sut.SelectedAccount = sut.Accounts.Single(account => account.Id == selectedId);
        await sut.GenerateCodeAsync();

        sut.SearchText = "Microsoft";

        Assert.Null(sut.SelectedAccount);
        Assert.False(sut.HasSelectedAccount);
        Assert.Empty(sut.GeneratedCode);
        Assert.Equal(0, sut.RemainingSeconds);
        Assert.Equal(0, sut.PeriodSeconds);
    }

    [Fact]
    public async Task SearchText_WithAutomaticGenerationEnabled_DoesNotSelectOrCopyFirstMatch()
    {
        var accountId = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(accountId, "GitHub", ValidSecret, "alice@example.test")
            ]));
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateManyAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Result.Ok(new AccountTotpGenerationBatch(
                new Dictionary<Guid, TotpGenerationResult>
                {
                    [accountId] = new("123456", 30, 30)
                },
                new HashSet<Guid>())));
        totp.Setup(value => value.GenerateAsync(accountId))
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
        sut.EnableAutomaticCodeGenerationOnSelection();
        await sut.LoadAsync();

        sut.SearchText = "git";
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Single(sut.Accounts);
        Assert.Null(sut.SelectedAccount);
        Assert.Empty(sut.Notification.Text);
        totp.Verify(value => value.GenerateAsync(It.IsAny<Guid>()), Times.Never);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
    public void SelectForKeyboardNavigation_WhenRepeated_SelectsWithoutCopying()
    {
        var accountId = Guid.NewGuid();
        var secondAccountId = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        var clipboard = SuccessfulClipboard();
        using var sut = new AccountListViewModel(
            Mock.Of<IAccountManager>(),
            totp.Object,
            clipboard.Object,
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization());
        var account = new AccountListItemViewModel(accountId, "Issuer", "account");
        account.UpdateCode("654321", 24, 30);
        var secondAccount = new AccountListItemViewModel(
            secondAccountId,
            "Second issuer",
            "second account");
        secondAccount.UpdateCode("123456", 18, 30);
        sut.EnableAutomaticCodeGenerationOnSelection();

        sut.SelectForKeyboardNavigation(account);
        sut.SelectForKeyboardNavigation(secondAccount);

        Assert.Same(secondAccount, sut.SelectedAccount);
        Assert.Equal("123456", sut.GeneratedCode);
        Assert.Equal(18, sut.RemainingSeconds);
        totp.Verify(value => value.GenerateAsync(It.IsAny<Guid>()), Times.Never);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
            Localization(),
            transientMessageDuration: TimeSpan.FromMilliseconds(50))
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };
        await sut.GenerateCodeAsync();

        await sut.CopyCodeAsync();

        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromSeconds(18),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Copied", sut.SelectedAccount.CopyConfirmation);
        Assert.Empty(sut.Notification.Text);
        Assert.Empty(sut.CodeMessage);

        await WaitUntilAsync(() => !sut.SelectedAccount.HasCopyConfirmation);
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

        Assert.Equal("Kopiert", sut.SelectedAccount.CopyConfirmation);
        Assert.Empty(sut.Notification.Text);
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
    public async Task SaveAccountAsync_PersistsFavoriteSelectionForNewAccount()
    {
        var manager = new Mock<IAccountManager>();
        Account? created = null;
        manager.SetupSequence(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]))
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(created is null ? [] : [created]));
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => created = account)
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "GitHub";
        sut.EditorSecret = ValidSecret;
        sut.EditorIsFavorite = true;

        await sut.SaveAccountAsync();

        Assert.NotNull(created);
        Assert.True(created.IsFavorite);
        Assert.True(Assert.Single(sut.Accounts).IsFavorite);
        Assert.False(sut.EditorIsFavorite);
    }

    [Fact]
    public async Task BeginEditAndSave_CanRemoveFavorite()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var original = new Account(
            Guid.NewGuid(),
            "GitHub",
            ValidSecret,
            "alice",
            group: group,
            isFavorite: true);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([original]));
        Account? updated = null;
        manager.Setup(value => value.UpdateAsync(original, It.IsAny<Account>()))
            .Callback<Account, Account>((_, account) => updated = account)
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object);
        await sut.LoadAsync();
        sut.Groups.Single().SelectCommand.Execute(null);
        sut.SelectedAccount = Assert.Single(sut.Accounts);

        await sut.BeginEditAsync();
        Assert.True(sut.EditorIsFavorite);
        sut.EditorIsFavorite = false;
        await sut.SaveAccountAsync();

        Assert.NotNull(updated);
        Assert.False(updated.IsFavorite);
        manager.Verify(value => value.UpdateAsync(
            original,
            It.Is<Account>(account => !account.IsFavorite)), Times.Once);
    }

    [Fact]
    public async Task EditAccount_OffersImportedIconsAndPersistsExplicitSelection()
    {
        var account = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        manager.Setup(value => value.UpdateAsync(account, It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.SetupGet(value => value.AvailableBrands).Returns(
        [
            new BrandDefinition("amazon", "Amazon", "#FF9900", "amazon.svg"),
            new BrandDefinition("github", "GitHub", "#181717", "github.svg")
        ]);
        brandIcons.Setup(value => value.GetAccountBrandId(account.ID)).Returns("amazon");
        brandIcons.Setup(value => value.SetAccountBrandIdAsync(
                account.ID, "github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(manager.Object, brandIconPackService: brandIcons.Object);
        await sut.LoadAsync();
        sut.SelectForKeyboardNavigation(Assert.Single(sut.Accounts));

        await sut.BeginEditAsync();

        Assert.True(sut.HasBrandIconChoices);
        Assert.Equal("amazon", sut.SelectedEditorBrandIconOption?.Id);
        sut.SelectedEditorBrandIconOption = Assert.Single(
            sut.EditorBrandIconOptions,
            option => option.Id == "github");
        await sut.SaveAccountAsync();

        brandIcons.Verify(value => value.SetAccountBrandIdAsync(
            account.ID, "github", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAccountAsync_WhenIconPreferenceFails_ReportsAccountWasStillSaved()
    {
        var manager = new Mock<IAccountManager>();
        Account? created = null;
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(
                created is null ? [] : [created]));
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => created = account)
            .ReturnsAsync(Result.Ok());
        var brandIcons = new Mock<IBrandIconPackService>();
        brandIcons.SetupGet(value => value.AvailableBrands).Returns(
        [
            new BrandDefinition("github", "GitHub", "#181717", "github.svg")
        ]);
        brandIcons.Setup(value => value.SetAccountBrandIdAsync(
                It.IsAny<Guid>(), "github", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("synthetic preference failure"));
        using var sut = CreateSut(manager.Object, brandIconPackService: brandIcons.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "GitHub";
        sut.EditorSecret = ValidSecret;
        sut.SelectedEditorBrandIconOption = Assert.Single(
            sut.EditorBrandIconOptions,
            option => option.Id == "github");

        await sut.SaveAccountAsync();

        Assert.NotNull(created);
        Assert.Equal(
            "Account saved, but its local icon preference could not be saved.",
            sut.Message);
    }

    [Fact]
    public async Task SaveEditedAccount_DoesNotGenerateOrCopyACode()
    {
        var account = new Account(Guid.NewGuid(), "GitHub", ValidSecret, "alice");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        manager.Setup(value => value.UpdateAsync(account, It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        var totp = new Mock<IAccountTotpService>();
        var clipboard = new Mock<IAsyncClipboardService>();
        using var sut = CreateSut(
            manager.Object,
            accountTotpService: totp.Object,
            clipboardService: clipboard.Object);
        await sut.LoadAsync();
        sut.SelectForKeyboardNavigation(Assert.Single(sut.Accounts));
        sut.EnableAutomaticCodeGenerationOnSelection();
        await sut.BeginEditAsync();

        await sut.SaveAccountAsync();

        Assert.Equal("Account saved.", sut.Message);
        clipboard.Verify(value => value.CopyAndScheduleClearAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task ClearEditorPeriodCommand_ClearsCurrentPeriodAndDisablesItself()
    {
        var sut = CreateSut(Mock.Of<IAccountManager>());
        await sut.BeginAddAsync();

        Assert.Equal(30, sut.EditorPeriodSeconds);
        Assert.True(sut.HasEditorPeriodSeconds);
        Assert.True(sut.ClearEditorPeriodCommand.CanExecute(null));

        sut.ClearEditorPeriodCommand.Execute(null);

        Assert.Null(sut.EditorPeriodSeconds);
        Assert.False(sut.HasEditorPeriodSeconds);
        Assert.False(sut.ClearEditorPeriodCommand.CanExecute(null));
    }

    [Fact]
    public void AccountRow_EntersWarningStateAtTenSecondsAndLeavesItAfterRefresh()
    {
        var sut = new AccountListItemViewModel(Guid.NewGuid(), "Issuer", "account");

        sut.UpdateCode("123456", 11, 30);
        Assert.False(sut.IsExpiring);

        sut.Tick();
        Assert.True(sut.IsExpiring);

        sut.UpdateCode("654321", 30, 30);
        Assert.False(sut.IsExpiring);
    }

    [Fact]
    public async Task CountdownAsync_KeepsCurrentRowVisibleUntilReplacementCodeIsReady()
    {
        var id = Guid.NewGuid();
        var replacement = new TaskCompletionSource<Result<AccountTotpGenerationBatch>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateManyAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == id)))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return Task.FromResult(Result.Ok(new AccountTotpGenerationBatch(
                        new Dictionary<Guid, TotpGenerationResult>
                        {
                            [id] = new("111111", 1, 30)
                        },
                        new HashSet<Guid>())));
                }

                replacementRequested.TrySetResult();
                return replacement.Task;
            });
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(id, "Issuer", ValidSecret, "account")
            ]));
        using var sut = new AccountListViewModel(
            manager.Object,
            totp.Object,
            Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            Mock.Of<IAvaloniaDialogService>(),
            Localization(),
            TimeSpan.FromMilliseconds(10));
        sut.EnableAutomaticCodeGenerationOnSelection();

        await sut.LoadAsync();
        await replacementRequested.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        var row = Assert.Single(sut.Accounts);
        Assert.True(row.HasCode);
        Assert.Equal("111 111", row.DisplayCode);
        Assert.Equal(1, row.RemainingSeconds);

        replacement.SetResult(Result.Ok(new AccountTotpGenerationBatch(
            new Dictionary<Guid, TotpGenerationResult>
            {
                [id] = new("222222", 30, 30)
            },
            new HashSet<Guid>())));
        await WaitUntilAsync(() => row.Code == "222222");
    }

    [Fact]
    public async Task CopyCodeAsync_WhenLegacySettingDisabled_StillSchedulesRequiredClear()
    {
        var id = Guid.NewGuid();
        var totp = new Mock<IAccountTotpService>();
        totp.Setup(value => value.GenerateAsync(id))
            .ReturnsAsync(Result.Ok(new TotpGenerationResult("123456", 30, 30)));
        var clipboard = new Mock<IAsyncClipboardService>();
        clipboard.Setup(value => value.CopyAndScheduleClearAsync(
                "123456",
                TimeSpan.FromSeconds(15),
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
            "123456", TimeSpan.FromSeconds(15), It.IsAny<CancellationToken>()), Times.Once);
        clipboard.Verify(value => value.CopyAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Copied", sut.SelectedAccount.CopyConfirmation);
        Assert.Empty(sut.Notification.Text);
        Assert.Empty(sut.CodeMessage);
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
            settingsService: settings.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20))
        {
            SelectedAccount = new AccountListItemViewModel(id, "Issuer", "account")
        };
        await sut.GenerateCodeAsync();

        await sut.CopyCodeAsync();

        clipboard.Verify(value => value.CopyAsync(
            "123456", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Kopiert", sut.SelectedAccount.CopyConfirmation);
        Assert.Equal(
            "Kopiert. Das automatische Leeren der Zwischenablage ist auf dieser Plattform nicht verfügbar.",
            sut.Notification.Text);
        Assert.Equal(NotificationSeverity.Warning, sut.Notification.Severity);
        Assert.Empty(sut.CodeMessage);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(sut.Notification.Text);
    }

    [Fact]
    public async Task SaveAccountAsync_WithGoogleAuthenticatorCompatibleShortSecret_PersistsAccount()
    {
        var manager = new Mock<IAccountManager>();
        Account? created = null;
        manager.SetupSequence(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]))
            .ReturnsAsync(() => Result.Ok<IReadOnlyList<Account>>(
                created is null ? [] : [created]));
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account account) =>
            {
                created = account;
                return Result.Ok();
            });
        var sut = CreateSut(manager.Object);
        await sut.BeginAddAsync();
        sut.EditorIssuer = "Example";
        sut.EditorSecret = "ORSXG5A";

        await sut.SaveAccountAsync();

        Assert.NotNull(created);
        Assert.Equal("ORSXG5A", created.Secret);
    }

    private static AccountListViewModel CreateSut(
        IAccountManager manager,
        IAvaloniaDialogService? dialogs = null,
        TimeSpan? transientMessageDuration = null,
        IAccountTotpService? accountTotpService = null,
        IAsyncClipboardService? clipboardService = null,
        IBrandIconResolver? brandIconResolver = null,
        IBrandIconPackService? brandIconPackService = null,
        ISettingsService? settingsService = null,
        IAvaloniaLocalizationService? localization = null) =>
        new(
            manager,
            accountTotpService ?? Mock.Of<IAccountTotpService>(),
            clipboardService ?? Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(),
            Mock.Of<IAvaloniaQrImageFactory>(),
            dialogs ?? Mock.Of<IAvaloniaDialogService>(),
            localization ?? Localization(),
            transientMessageDuration: transientMessageDuration,
            settingsService: settingsService,
            brandIconResolver: brandIconResolver,
            brandIconPackService: brandIconPackService);

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

    private static IAvaloniaLocalizationService Localization(string cultureName = "en")
    {
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture(cultureName);
        return localization;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }
}
