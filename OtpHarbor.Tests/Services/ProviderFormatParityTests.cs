using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class ProviderFormatParityTests
{
    private const string Seed = "JBSWY3DPEHPK3PXP";
    private const string UriValue = "otpauth://totp/Example:alice%40example.test?secret=" + Seed + "&issuer=Example&algorithm=SHA512&digits=8&period=60";

    // Schema provenance and intentional differences are recorded in AUTHENTICATOR_IMPORT_PARITY.md.
    private static JsonObject TwoFas(int version = 4) => JsonSerializer.SerializeToNode(new
    {
        appOrigin = "ios", appVersionCode = 200208, appVersionName = "2.2.8", schemaVersion = version,
        services = new[] { new { name = "Example", secret = Seed, type = "custom", order = new { position = 0 },
            otp = new { account = "alice@example.test", issuer = "Example", algorithm = "SHA512", digits = 8, period = 60, tokenType = "TOTP" } } },
        servicesEncrypted = (string?)null, reference = (string?)null, groups = Array.Empty<object>()
    })!.AsObject();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OfficialTwoFasSchemas_PreserveSupportedFieldsWithoutDependingOnExtension(int version)
    {
        var result = await Read(TwoFas(version).ToJsonString(), version == 1 ? "legacy.bak" : "renamed.bin");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice@example.test", account.AccountName);
        Assert.Equal(Seed, account.Secret);
        Assert.Equal("SHA512", account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.PeriodSeconds);
        Assert.Empty(account.Groups);
        Assert.Equal(AccountImportFormat.TwoFasJson, Summary(result).Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task TwoFasBlankEncryptionFields_RemainPlaintextAndUseDocumentedDefaults(string? marker)
    {
        var root = TwoFas(1);
        root["servicesEncrypted"] = marker;
        root["reference"] = marker;
        var otp = root["services"]![0]!["otp"]!.AsObject();
        foreach (var field in new[] { "algorithm", "digits", "period", "account", "issuer", "tokenType" }) otp[field] = null;
        var result = await Read(root.ToJsonString());
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Example", account.Issuer);
        Assert.Null(account.AccountName);
        Assert.Equal("SHA1", account.Algorithm);
        Assert.Equal(6, account.Digits);
        Assert.Equal(30, account.PeriodSeconds);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("servicesEncrypted")]
    public async Task TwoFasEncryptedMarker_RejectsEvenWhenPlainServicesAreAlsoPresent(string marker)
    {
        var root = TwoFas();
        root[marker] = "synthetic-encrypted-marker";
        var result = await Read(root.ToJsonString());
        Assert.True(result.IsFailed);
        Assert.Equal(AccountImportFormat.EncryptedExternalExport, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.Format);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task TwoFasUnknownSchema_FailsClosed(int version) =>
        Assert.True((await Read(TwoFas(version).ToJsonString())).IsFailed);

    [Theory]
    [InlineData("empty")]
    [InlineData("issuer")]
    [InlineData("account")]
    public async Task TwoFasInvalidIdentity_IsRejectedBeforeReview(string invalid)
    {
        var root = TwoFas();
        var service = root["services"]![0]!.AsObject();
        var otp = service["otp"]!.AsObject();
        if (invalid == "empty") { service["name"] = " "; otp["issuer"] = " "; }
        else otp[invalid == "issuer" ? "issuer" : "account"] = new string('x', 257);
        var result = await Read(root.ToJsonString());
        Assert.True(result.IsFailed);
        Assert.DoesNotContain(Seed, string.Join(' ', result.Errors.Select(error => error.Message)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderDuplicateNestedFields_AreRejected(bool twoFas)
    {
        var json = twoFas ? TwoFas().ToJsonString()
            : "{\"version\":1,\"db\":{\"version\":3,\"entries\":[{\"type\":\"totp\",\"issuer\":\"Example\",\"name\":\"alice\",\"info\":{\"secret\":\"" + Seed + "\",\"algo\":\"SHA1\",\"digits\":6,\"period\":30}}]}}";
        json = json.Replace("\"secret\":", "\"secret\":\"KRSXG5DSNFXGOIDB\",\"secret\":", StringComparison.Ordinal);
        var result = await Read(json);
        Assert.True(result.IsFailed);
        Assert.DoesNotContain(Seed, string.Join(' ', result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public async Task BitwardenPasswordProtection_CannotBeHiddenByAPlaintextFlag()
    {
        var json = JsonSerializer.Serialize(new { encrypted = false, passwordProtected = true,
            items = new[] { new { type = 1, name = "Example", login = new { totp = UriValue } } } });
        var result = await Read(json);
        Assert.True(result.IsFailed);
        Assert.Equal(AccountImportFormat.EncryptedExternalExport, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.Format);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("null")]
    [InlineData("1")]
    public async Task BitwardenMalformedPasswordProtectionFlag_IsNotTreatedAsUnencrypted(string flag)
    {
        var root = JsonSerializer.SerializeToNode(new { encrypted = false,
            items = new[] { new { type = 1, name = "Example", login = new { totp = UriValue } } } })!.AsObject();
        root["passwordProtected"] = JsonNode.Parse(flag);
        var result = await Read(root.ToJsonString());
        Assert.True(result.IsFailed);
        Assert.Equal(1, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.InvalidCount);
    }

    [Fact]
    public async Task OfficialBitwardenIosCsv_BlankRecordSeparatorsDoNotChangeTheImportedAccounts()
    {
        var csv = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n"
            + ",1,login,Example,,,0,,alice@example.test,," + Seed + "\n\n"
            + ",,login,Second,,,0,,bob@example.test,," + Seed + "\n";
        var result = await Read(csv, "renamed.json");
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.True(result.Value[0].IsFavorite);
        Assert.Equal("bob@example.test", result.Value[1].AccountName);
    }

    [Fact]
    public async Task EnteDeletedEntries_AreExcludedAndCountedWithoutImportingDisplayMetadata()
    {
        var note = "private-note-sentinel&secret=OTHER&period=1#\nÜ,雪";
        var active = EnteUri(false, note);
        var deleted = EnteUri(true, note);
        var result = await Read(active + "\n" + deleted + "\n", "renamed.csv");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal(Seed, account.Secret);
        Assert.Equal("alice@example.test", account.AccountName);
        Assert.Equal("SHA512", account.Algorithm);
        Assert.Equal(60, account.PeriodSeconds);
        Assert.Equal(8, account.Digits);
        Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(account));
        var summary = Summary(result);
        Assert.Equal(1, summary.SupportedCount);
        Assert.Equal(1, summary.TrashedCount);
        Assert.Equal(0, summary.NonOtpCount);
    }

    [Fact]
    public async Task EnteOnlyDeletedEntries_ReturnNoActiveAccountsWithExplicitDiagnostics()
    {
        var result = await Read(EnteUri(true) + "\r\n" + EnteUri(true));
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Equal(2, Summary(result).TrashedCount);
        Assert.Equal(0, Summary(result).SupportedCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"trashed\":\"true\"}")]
    [InlineData("{\"trashed\":true,\"trashed\":false}")]
    public async Task EnteAmbiguousDeletedState_FailsWithoutReturningPartialAccounts(string display)
    {
        var result = await Read(UriValue + "\n" + UriValue + "&codeDisplay=" + Uri.EscapeDataString(display));
        Assert.True(result.IsFailed);
        var summary = Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary;
        Assert.Equal(1, summary.SupportedCount);
        Assert.Equal(1, summary.InvalidCount);
        Assert.DoesNotContain(Seed, string.Join(' ', result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public async Task EnteRepeatedDisplayParameters_AreNotInterpretedUsingFirstOrLastWins()
    {
        var result = await Read(EnteUri(true) + "&codeDisplay=" + Uri.EscapeDataString("{\"trashed\":false}"));
        Assert.True(result.IsFailed);
        Assert.Equal(1, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.InvalidCount);
    }

    [Fact]
    public async Task DeletedMetadata_DoesNotHideANonOtpUri()
    {
        var result = await Read(UriValue + "\n" + EnteUri(true).Replace("otpauth://", "https://", StringComparison.Ordinal));
        Assert.True(result.IsFailed);
        var summary = Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary;
        Assert.Equal(1, summary.InvalidCount);
        Assert.Equal(0, summary.TrashedCount);
    }

    [Fact]
    public async Task EnteMixedMalformedAndDeletedEntries_PreserveCountsButNeverReturnPartialData()
    {
        var result = await Read(UriValue + "\n" + EnteUri(true) + "\n" + UriValue.Replace(Seed, "invalid!", StringComparison.Ordinal));
        Assert.True(result.IsFailed);
        var summary = Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary;
        Assert.Equal(1, summary.SupportedCount);
        Assert.Equal(1, summary.TrashedCount);
        Assert.Equal(1, summary.InvalidCount);
    }

    private static string EnteUri(bool trashed, string note = "ignored") => UriValue + "&codeDisplay="
        + Uri.EscapeDataString(JsonSerializer.Serialize(new { trashed, note, pinned = true, tags = new[] { "ignored" } }));
    private static AccountImportReadSummary Summary(FluentResults.Result<List<Account>> result) => Assert.Single(result.Successes.OfType<AccountImportReadSummary>());
    private static async Task<FluentResults.Result<List<Account>>> Read(string content, string name = "renamed.bin")
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await service.ImportFromStreamWithFormatSelectionAsync(input, name,
            (_, _) => throw new InvalidOperationException("Unexpected format selection"),
            (_, _) => throw new InvalidOperationException("Foreign exports must not use the native backup password prompt"), TestContext.Current.CancellationToken);
    }
}
