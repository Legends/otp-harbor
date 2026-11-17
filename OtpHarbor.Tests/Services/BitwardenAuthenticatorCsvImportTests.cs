using System.Globalization;
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

public sealed class BitwardenAuthenticatorCsvImportTests
{
    private const string Header = "folder,favorite,type,name,login_uri,login_totp";
    private const string Seed = "JBSWY3DPEHPK3PXP"; // gitleaks:allow - synthetic fixture
    private readonly ExportService _reader = new(NullLogger<ExportService>.Instance);

    [Theory]
    [InlineData("authenticator.csv")]
    [InlineData("renamed.json")]
    [InlineData("no-extension")]
    public async Task AndroidAuthenticatorExport_MatchesJsonAndReachesReviewWithoutWriting(string fileName)
    {
        var entries = Enumerable.Range(0, 17).Select(index =>
        {
            var issuer = index == 0 ? "München, \"Lab\"" : "Synthetic " + index;
            var username = index == 1 ? "" : "user-" + index + "@example.invalid";
            var period = index == 0 ? 60 : index == 1 ? 15 : index == 13 ? 10 : 30;
            var algorithm = index == 0 ? "SHA512" : index == 1 ? "SHA256" : "SHA1";
            var digits = index == 0 ? 8 : 6;
            var seed = Seed[..^1] + "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[index];
            var uri = MakeUri(issuer, username, period, digits, algorithm, seed);
            return new { issuer, uri, period, digits };
        }).ToArray();
        var csv = Header + "\r\n" + string.Join("\r\n", entries.Select(entry =>
            Row(entry.issuer, entry.uri, entry.period.ToString(CultureInfo.InvariantCulture), entry.digits.ToString(CultureInfo.InvariantCulture))));
        var json = JsonSerializer.Serialize(new
        {
            encrypted = false,
            items = entries.Select(entry => new { type = 1, name = entry.issuer, login = new { totp = entry.uri } })
        });
        var decoded = await Read(csv, fileName);
        var jsonDecoded = await Read(json, "authenticator.json");
        Assert.True(decoded.IsSuccess);
        Assert.True(jsonDecoded.IsSuccess);
        Assert.Equal(17, decoded.Value.Count);
        Assert.Equal(17, decoded.Value.Select(account => account.ID).Distinct().Count());
        for (var index = 0; index < entries.Length; index++)
        {
            var actual = decoded.Value[index];
            var expected = jsonDecoded.Value[index];
            Assert.Equal(expected.Issuer, actual.Issuer);
            Assert.Equal(expected.AccountName, actual.AccountName);
            Assert.Equal(expected.Secret, actual.Secret);
            Assert.Equal(expected.Algorithm, actual.Algorithm);
            Assert.Equal(expected.Digits, actual.Digits);
            Assert.Equal(expected.PeriodSeconds, actual.PeriodSeconds);
            Assert.Equal(expected.IsFavorite, actual.IsFavorite);
        }
        var summary = Assert.Single(decoded.Successes.OfType<AccountImportReadSummary>());
        Assert.Equal(AccountImportFormat.BitwardenCsv, summary.Format);
        Assert.Equal(17, summary.SupportedCount);
        Assert.Equal(0, summary.InvalidCount + summary.UnsupportedCount + summary.NonOtpCount);

        var manager = new Mock<IAccountManager>(MockBehavior.Strict);
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var importer = new AccountImportService(manager.Object, NullLogger<AccountImportService>.Instance);
        var outcome = await importer.ImportWithConflictResolutionAsync(decoded.Value, (preview, _) =>
        {
            Assert.Equal(17, preview.Entries.Count);
            return Task.FromResult<AccountImportResolution?>(null);
        }, TestContext.Current.CancellationToken);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(AccountImportStatus.Cancelled, outcome.Value.Status);
        manager.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Once);
        manager.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("login")]
    [InlineData("1")]
    public async Task SixDeclaredColumns_AcceptLoginTypeAndPreserveUriParameters(string type)
    {
        var result = await Read(Header + "\n" + string.Join(',', new[]
        {
            "", "1", type, "Demo", "", MakeUri("Demo", "user", 60, 8, "SHA512")
        }.Select(Quote)), "changed.bin");
        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.True(account.IsFavorite);
        Assert.Equal("SHA512", account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.PeriodSeconds);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("name")]
    [InlineData("period")]
    [InlineData("digits")]
    [InlineData("invalid-period")]
    [InlineData("invalid-digits")]
    [InlineData("extra-column")]
    [InlineData("missing-column")]
    [InlineData("raw-secret")]
    [InlineData("text-login-with-suffix")]
    [InlineData("non-login-with-suffix")]
    public async Task ContradictoryOrMalformedSuffix_RejectsWholeFile(string problem)
    {
        var uri = MakeUri("Demo", "user", 60, 8, "SHA512");
        var fields = new List<string> { "", "", "1", "Demo", "", uri, "Demo", "60", "8" };
        switch (problem)
        {
            case "issuer": fields[6] = "Different"; break;
            case "name": fields[3] = "Different"; break;
            case "period": fields[7] = "30"; break;
            case "digits": fields[8] = "6"; break;
            case "invalid-period": fields[7] = "not-a-period"; break;
            case "invalid-digits": fields[8] = "not-digits"; break;
            case "extra-column": fields.Add("unexpected-private-sentinel"); break;
            case "missing-column": fields.RemoveAt(8); break;
            case "raw-secret": fields[5] = Seed; break;
            case "text-login-with-suffix": fields[2] = "login"; break;
            case "non-login-with-suffix": fields[2] = "2"; break;
        }
        var result = await Read(Header + "\n" + Row("Demo", uri, "60", "8") + "\n" + string.Join(',', fields.Select(Quote)), "input.csv");
        Assert.True(result.IsFailed);
        var summary = Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary;
        Assert.Equal(AccountImportFormat.BitwardenCsv, summary.Format);
        Assert.Equal(1, summary.SupportedCount);
        Assert.Equal(1, summary.InvalidCount);
        Assert.DoesNotContain("unexpected-private-sentinel", string.Join(" ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public async Task PasswordVaultHeader_DoesNotAcceptUnlabelledExtraColumns()
    {
        var header = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp";
        var result = await Read(header + "\n" + string.Join(',', new[]
        {
            "", "", "1", "Demo", "", "", "0", "", "", "", MakeUri("Demo", "user", 60, 8, "SHA512"), "Demo", "60", "8"
        }.Select(Quote)), "vault.csv");
        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task DuplicateHeaders_RejectWithoutChoosingAColumn()
    {
        var result = await Read(Header + ",LOGIN_TOTP\n" + Row("Demo", MakeUri("Demo", "user", 60, 8, "SHA512"), "60", "8"), "input.csv");
        Assert.True(result.IsFailed);
    }

    private async Task<Result<List<Account>>> Read(string content, string name)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await _reader.ImportFromStreamAsync(stream, name, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Row(string issuer, string uri, string period, string digits) =>
        string.Join(',', new[] { "", "", "1", issuer, "", uri, issuer, period, digits }.Select(Quote));
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string MakeUri(string issuer, string username, int period, int digits, string algorithm, string seed = Seed) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer + ":" + username)}?secret={seed}&issuer={Uri.EscapeDataString(issuer)}&period={period}&digits={digits}&algorithm={algorithm}";
}
