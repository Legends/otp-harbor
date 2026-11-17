using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class LastPassAuthenticatorImportTests
{
    private const string Seed = "JBSWY3DPEHPK3PXP";
    private static JsonObject Export(string issuer = "Example", string? name = "alice@example.test", string algorithm = "SHA512", int digits = 8, int period = 60) =>
        JsonSerializer.SerializeToNode(new { version = 1, accounts = new[] { new { issuerName = issuer, userName = name, secret = Seed,
            algorithm, digits, timeStep = period, notes = "ignored", password = "ignored", id = "foreign-id" } } })!.AsObject();

    [Theory]
    [InlineData("backup.json", "SHA1", 6, 30)]
    [InlineData("renamed.bin", "SHA256", 8, 60)]
    [InlineData("misleading.csv", "SHA512", 8, 120)]
    public async Task PlaintextLastPass_PreservesSupportedFieldsRegardlessOfExtension(string fileName, string algorithm, int digits, int period)
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Export(algorithm: algorithm, digits: digits, period: period).ToJsonString()));
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, fileName,
            (_, _) => throw new InvalidOperationException("Unexpected ambiguity"),
            (_, _) => throw new InvalidOperationException("Unexpected password prompt"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice@example.test", account.AccountName);
        Assert.Equal(Seed, account.Secret);
        Assert.Equal(algorithm, account.Algorithm);
        Assert.Equal(digits, account.Digits);
        Assert.Equal(period, account.PeriodSeconds);
        Assert.NotEqual(Guid.Empty, account.ID);
        Assert.Empty(account.Groups);
        Assert.False(account.IsFavorite);
        Assert.Equal(AccountImportFormat.LastPassJson, Assert.Single(result.Successes.OfType<AccountImportReadSummary>()).Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OptionalAccountName_MayBeEmpty(string? name)
    {
        var result = await Read(Export(name: name));
        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value).AccountName);
    }

    [Fact]
    public async Task MissingIssuer_UsesNonemptyDisplayNameAsIssuerLikeOtherSupportedExports()
    {
        var result = await Read(Export(issuer: "", name: "Display name"));
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Display name", account.Issuer);
        Assert.Null(account.AccountName);
    }

    [Theory]
    [InlineData("algorithm", "MD5", true)]
    [InlineData("digits", "7", true)]
    [InlineData("timeStep", "0", true)]
    [InlineData("secret", "not-base32!", false)]
    [InlineData("secret", "otpauth://totp/Other?secret=JBSWY3DPEHPK3PXP", false)]
    public async Task InvalidOrUnsupportedEntry_RejectsEntireMixedFile(string field, string value, bool unsupported)
    {
        var export = Export();
        var entries = export["accounts"]!.AsArray();
        var bad = entries[0]!.DeepClone().AsObject();
        bad[field] = field is "digits" or "timeStep" ? JsonValue.Create(int.Parse(value)) : JsonValue.Create(value);
        entries.Add(bad);
        var result = await Read(export);
        Assert.True(result.IsFailed);
        var summary = Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary;
        Assert.Equal(1, summary.SupportedCount);
        Assert.Equal(unsupported ? 1 : 0, summary.UnsupportedCount);
        Assert.Equal(unsupported ? 0 : 1, summary.InvalidCount);
        Assert.DoesNotContain(Seed, string.Join(" ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public async Task UnsupportedVersionAndEncryptedExports_FailClosed()
    {
        var version = Export(); version["version"] = 2;
        Assert.True((await Read(version)).IsFailed);
        var encrypted = Export(); encrypted["encrypted"] = true;
        var result = await Read(encrypted);
        Assert.True(result.IsFailed);
        Assert.Equal(AccountImportFormat.EncryptedExternalExport, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.Format);
    }

    private static async Task<FluentResults.Result<List<TOTP.Core.Models.Account>>> Read(JsonObject json)
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        return await service.ImportFromStreamWithFormatSelectionAsync(stream, "renamed.data",
            (_, _) => throw new InvalidOperationException("Unexpected ambiguity"), TestContext.Current.CancellationToken);
    }
}
