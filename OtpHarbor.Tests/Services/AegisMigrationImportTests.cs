using System.Text;
using System.Text.Json;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AegisMigrationImportTests
{
    private const string Seed = "JBSWY3DPEHPK3PXP"; // gitleaks:allow - synthetic fixture
    private readonly ExportService _reader = new(NullLogger<ExportService>.Instance);

    [Theory]
    [InlineData("aegis-export.json")]
    [InlineData("renamed.csv")]
    [InlineData("no-extension")]
    public async Task Aegis343_PromotesTheSoleLabelAndReachesSharedConflictReviewWithoutWriting(string fileName)
    {
        var entries = Enumerable.Range(0, 17).Select(index => Entry(
            index == 11 ? "" : $"Issuer {index}", index == 11 ? " Sole account label " : $"user-{index}",
            period: index == 13 ? 10 : index == 16 ? 60 : 30,
            algorithm: index == 16 ? "SHA512" : "SHA1", digits: index == 16 ? 8 : 6,
            secret: Seed[..^1] + "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[index])).ToArray();
        var decoded = await Read(Vault(entries), fileName);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(17, decoded.Value.Count);
        Assert.Equal(17, decoded.Value.Select(account => account.ID).Distinct().Count());
        Assert.All(decoded.Value, account => Assert.False(string.IsNullOrWhiteSpace(account.Issuer)));
        Assert.Equal("Sole account label", decoded.Value[11].Issuer);
        Assert.Null(decoded.Value[11].AccountName);
        Assert.Equal("Issuer 0", decoded.Value[0].Issuer);
        Assert.Equal("user-0", decoded.Value[0].AccountName);
        Assert.Equal(10, decoded.Value[13].PeriodSeconds);
        Assert.Equal("SHA512", decoded.Value[16].Algorithm);
        Assert.Equal(8, decoded.Value[16].Digits);
        Assert.Equal(60, decoded.Value[16].PeriodSeconds);
        Assert.Equal(AccountImportFormat.AegisJson, Assert.Single(decoded.Successes.OfType<AccountImportReadSummary>()).Format);
        Assert.DoesNotContain("synthetic-note-sentinel", JsonSerializer.Serialize(decoded.Value));

        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var importer = new AccountImportService(accounts.Object, NullLogger<AccountImportService>.Instance);
        AccountImportPreview? reviewed = null;
        var result = await importer.ImportWithConflictResolutionAsync(decoded.Value, (preview, _) =>
        {
            reviewed = preview;
            return Task.FromResult<AccountImportResolution?>(null);
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(reviewed);
        Assert.Equal(17, reviewed.NewCount);
        Assert.Equal(17, reviewed.Entries.Count);
        Assert.Equal(AccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Once);
        accounts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankIssuer_PromotesTrimmedLabelWithoutInventingOrDuplicatingIdentity(string issuer)
    {
        var result = await Read(Vault([Entry(issuer, "  Label only  ")]), "aegis.json");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Label only", account.Issuer);
        Assert.Null(account.AccountName);
        Assert.Equal(Seed, account.Secret);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" ", "\t")]
    [InlineData("", "valid", "INVALID1")]
    public async Task MissingIdentityOrMalformedSecret_FailsTheWholeFile(string issuer, string name, string secret = Seed)
    {
        var result = await Read(Vault([Entry("Valid issuer", "user"), Entry(issuer, name, secret: secret)]), "aegis.json");
        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task ExcessiveFallbackIdentity_FailsTheWholeFile()
    {
        var result = await Read(Vault([Entry("Valid issuer", "user"), Entry("", new string('x', 257))]), "aegis.json");
        Assert.True(result.IsFailed);
    }

    private async Task<Result<List<Account>>> Read(string content, string fileName)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await _reader.ImportFromStreamAsync(stream, fileName, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static object Entry(string issuer, string name, int period = 30, string algorithm = "SHA1", int digits = 6, string secret = Seed) => new
    {
        type = "totp", uuid = Guid.NewGuid().ToString(), issuer, name,
        note = "synthetic-note-sentinel", favorite = false, groups = Array.Empty<string>(), icon = (string?)null,
        info = new { secret, algo = algorithm, digits, period }
    };

    private static string Vault(object[] entries) => JsonSerializer.Serialize(new
    {
        version = 1, header = new { slots = (object?)null, @params = (object?)null },
        db = new { version = 3, entries, groups = Array.Empty<object>(), icons_optimized = true }
    });
}
