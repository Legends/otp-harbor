using System.Text.Json.Nodes;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Services.Models;
using TOTP.Tests.Common;

namespace TOTP.Tests.Services;

public sealed partial class SimpleIconsBrandIconPackServiceTests
{
    [Fact]
    public async Task AccountStartup_PreparesAllRequestedMatchesBeforeLoadingTheUnusedCatalog()
    {
        using var temp = new TempDir();
        await using var archive = CreateOtpHarborV3Archive();
        Assert.True((await CreateSut(temp.Path).ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        var indexPath = Directory.GetFiles(temp.Path, "brand-index.json", SearchOption.AllDirectories).Single();
        var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken))!;
        var unused = index["Brands"]![0]!.DeepClone();
        unused["Id"] = "unused-brand";
        unused["DisplayName"] = "Unused brand";
        unused["Aliases"] = new JsonArray("Unused brand");
        index["Brands"]!.AsArray().Add(unused);
        index["Metadata"]!["IssuerAliases"]!.AsArray().Add(new JsonObject
        {
            ["Key"] = "unused brand", ["BrandId"] = "unused-brand"
        });
        await File.WriteAllTextAsync(indexPath, index.ToJsonString(), TestContext.Current.CancellationToken);
        var sut = CreateSut(temp.Path);

        await sut.InitializeForAccountsAsync(Enumerable.Repeat("C++", 40).Append("Unknown").ToArray(), TestContext.Current.CancellationToken);

        var prepared = Assert.IsType<BrandDefinition>(sut.Resolve("C++"));
        Assert.NotNull(prepared.RuntimeAppearances);
        Assert.NotNull(await RuntimeBrandIconAssetReader.ReadValidatedAsync(
            prepared.RuntimeAppearances.LightTheme.Asset, TestContext.Current.CancellationToken));
        Assert.Null(sut.Resolve("Unused brand"));
        Assert.Null(sut.Resolve("Unknown"));

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("unused-brand", sut.Resolve("Unused brand")?.Id);
        Assert.Equal(2, sut.AvailableBrands.Count);
        Assert.Equal(prepared, sut.Resolve("C++"));
    }

    [Fact]
    public async Task AccountStartup_PreservesExplicitMappingsAndDisplayPreference()
    {
        using var temp = new TempDir();
        var installed = CreateSut(temp.Path);
        await using var archive = CreateOtpHarborV3Archive();
        Assert.True((await installed.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        var accountId = Guid.NewGuid();
        Assert.True((await installed.SetAccountBrandIdAsync(accountId, "c-plus-plus", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await installed.SetShowIssuerLogoAsync(false, TestContext.Current.CancellationToken)).IsSuccess);
        var sut = CreateSut(temp.Path);

        await sut.InitializeForAccountsAsync(["Unrelated issuer"], TestContext.Current.CancellationToken);

        Assert.False(sut.ShowIssuerLogo);
        Assert.Equal("c-plus-plus", sut.GetAccountBrandId(accountId));
        Assert.Equal("c-plus-plus", sut.ResolveAccount(
            "Unrelated issuer", "unused account label", sut.GetAccountBrandId(accountId))?.Id);
        Assert.Null(sut.ResolveAccount("Unrelated issuer", "C++"));
    }

    [Fact]
    public async Task AccountStartup_WithNoMatchingIssuers_DoesNotRequireTheFullCatalog()
    {
        using var temp = new TempDir();
        await using var archive = CreateOtpHarborV3Archive();
        Assert.True((await CreateSut(temp.Path).ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        var sut = CreateSut(temp.Path);

        await sut.InitializeForAccountsAsync(["Unknown"], TestContext.Current.CancellationToken);

        Assert.Null(sut.Resolve("Unknown"));
        Assert.Null(sut.Resolve("C++"));
        await sut.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(sut.Resolve("C++"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccountStartup_RejectsUnsafeSelectedAssetsAndCorruptAliasReferences(bool unsafeAsset)
    {
        using var temp = new TempDir();
        await using var archive = CreateOtpHarborV3Archive();
        Assert.True((await CreateSut(temp.Path).ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        var indexPath = Directory.GetFiles(temp.Path, "brand-index.json", SearchOption.AllDirectories).Single();
        var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken))!;
        if (unsafeAsset)
            index["Brands"]![0]!["RuntimeAppearances"]!["LightTheme"]!["Asset"]!["ArchiveEntryPath"] = "../unsafe.png";
        else
            index["Metadata"]!["IssuerAliases"]![0]!["BrandId"] = "missing-brand";
        await File.WriteAllTextAsync(indexPath, index.ToJsonString(), TestContext.Current.CancellationToken);
        var sut = CreateSut(temp.Path);

        await sut.InitializeForAccountsAsync(["C++"], TestContext.Current.CancellationToken);

        Assert.Null(sut.Resolve("C++"));
    }
}
