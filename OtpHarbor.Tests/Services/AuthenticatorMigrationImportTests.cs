using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AuthenticatorMigrationImportTests
{
    private const string UriValue = "otpauth://totp/Demo:user%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Demo&algorithm=SHA512&digits=8&period=60";
    private readonly ExportService _service = new(NullLogger<ExportService>.Instance);

    [Theory]
    [InlineData("renamed.csv")]
    [InlineData("unknown.backup")]
    [InlineData("no-extension")]
    public async Task BitwardenJson_DetectsContentAndOnlyImportsSupportedTotpFields(string name)
    {
        var json = JsonSerializer.Serialize(new
        {
            encrypted = false,
            items = new object[]
            {
                new { type = 1, name = "Demo", favorite = true, notes = "private-note-sentinel",
                    login = new { username = "user@example.com", password = "private-password-sentinel", totp = UriValue } },
                new { type = 1, name = "Password only", login = new { password = "private-password-sentinel", totp = (string?)null } }
            }
        });
        var result = await Read(json, name);
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Demo", account.Issuer);
        Assert.Equal("user@example.com", account.AccountName);
        Assert.Equal("SHA512", account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.PeriodSeconds);
        Assert.True(account.IsFavorite);
        var summary = Assert.Single(result.Successes.OfType<AccountImportReadSummary>());
        Assert.Equal(1, summary.NonOtpCount);
        Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(account));
    }

    [Fact]
    public async Task BitwardenCsv_HandlesQuotedMultilineFieldsAndRawSeeds()
    {
        var csv = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n"
            + "Work,1,login,Demo,\"private\nnote\",,0,,user@example.com,password-sentinel,JBSWY3DPEHPK3PXP\n"
            + "Work,,note,No TOTP,ignore,,0,,,,\n";
        var result = await Read(csv, "renamed.json");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Demo", account.Issuer);
        Assert.Equal("user@example.com", account.AccountName);
        Assert.True(account.IsFavorite);
        Assert.Equal(1, Assert.Single(result.Successes.OfType<AccountImportReadSummary>()).NonOtpCount);
    }

    [Fact]
    public async Task ProtonJson_UsesVersionedContentUriAndExcludesNotes()
    {
        var result = await Read(Proton("Totp", UriValue), "proton.txt");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("SHA512", account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.PeriodSeconds);
        Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(account));
    }

    [Fact]
    public async Task EntePlaintext_PreservesTotpParametersWithoutImportingMetadata()
    {
        var result = await Read("# synthetic export\n" + UriValue + "&codeDisplay=%7B%22note%22%3A%22private-sentinel%22%7D\n", "ente.json");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("SHA512", account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.PeriodSeconds);
        Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(account));
    }

    [Fact]
    public async Task UriAlgorithms_UseTheSharedNormalizationPolicy()
    {
        var result = await Read(UriValue.Replace("SHA512", "sha512", StringComparison.Ordinal), "provider.txt");
        Assert.True(result.IsSuccess);
        Assert.Equal("SHA512", Assert.Single(result.Value).Algorithm);
    }

    [Theory]
    [InlineData("otpauth://hotp/Demo?secret=JBSWY3DPEHPK3PXP&counter=1", 0, 1)]
    [InlineData("otpauth://totp/Demo?secret=INVALID1", 1, 0)]
    [InlineData("otpauth://totp/Demo?secret=JBSWY3DPEHPK3PXP&digits=7", 0, 1)]
    public async Task MixedTotpExports_ReportFailuresAndNeverReturnAPartialImport(string invalidUri, int invalid, int unsupported)
    {
        var result = await Read(UriValue + "\n" + invalidUri, "mixed.txt");
        Assert.True(result.IsFailed);
        var summary = Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary;
        Assert.Equal(1, summary.SupportedCount);
        Assert.Equal(invalid, summary.InvalidCount);
        Assert.Equal(unsupported, summary.UnsupportedCount);
    }

    [Theory]
    [InlineData("{\"encrypted\":true,\"items\":[]}")]
    [InlineData("{\"version\":1,\"salt\":\"synthetic\",\"encryptedData\":\"synthetic\"}")]
    [InlineData("{\"version\":2,\"entries\":[]}")]
    public async Task EncryptedOrUnknownProviderVersions_AreRejectedWithoutFallback(string json)
    {
        var result = await Read(json, "provider.json");
        Assert.True(result.IsFailed);
        Assert.Equal(1, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.UnsupportedCount);
    }

    [Fact]
    public async Task ProtonSteam_IsReportedAsUnsupported()
    {
        var result = await Read(Proton("Steam", UriValue), "proton.json");
        Assert.True(result.IsFailed);
        Assert.Equal(1, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.UnsupportedCount);
    }

    [Theory]
    [InlineData("{\"encrypted\":true,\"encrypted\":false,\"items\":[]}")]
    [InlineData("{\"encrypted\":false,\"items\":[],\"salt\":\"synthetic\"}")]
    public async Task AmbiguousEncryptionMetadata_IsRejected(string json)
    {
        var result = await Read(json, "provider.json");
        Assert.True(result.IsFailed);
    }

    private Task<FluentResults.Result<List<TOTP.Core.Models.Account>>> Read(string content, string name)
    {
        return ReadCore();
        async Task<FluentResults.Result<List<TOTP.Core.Models.Account>>> ReadCore()
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return await _service.ImportFromStreamAsync(stream, name, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private static string Proton(string type, string uri) => JsonSerializer.Serialize(new
    {
        version = 1,
        entries = new[] { new { id = "synthetic", note = "private-note-sentinel", content = new { uri, entry_type = type, name = "Display only" } } }
    });
}
