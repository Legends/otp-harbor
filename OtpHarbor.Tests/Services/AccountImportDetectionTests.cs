using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AccountImportDetectionTests
{
    private const string Seed = "JBSWY3DPEHPK3PXP";
    private const string UriValue = "otpauth://totp/Demo:user?secret=JBSWY3DPEHPK3PXP&issuer=Demo&algorithm=SHA512&digits=8&period=60";
    private const string Aegis = """{"version":1,"header":{"slots":null,"params":null},"db":{"version":3,"entries":[{"type":"totp","uuid":"00000000-0000-0000-0000-000000000001","issuer":"Aegis","name":"user","info":{"secret":"JBSWY3DPEHPK3PXP","algo":"SHA1","digits":6,"period":30}}]}}""";
    private const string TwoFas = """{"schemaVersion":4,"services":[{"name":"2FAS","secret":"JBSWY3DPEHPK3PXP","otp":{"issuer":"2FAS","account":"user","algorithm":"SHA1","digits":6,"period":30,"tokenType":"TOTP"}}]}""";
    private readonly ExportService _service = new(NullLogger<ExportService>.Instance);

    public static TheoryData<string, AccountImportFormat> Supported => new()
    {
        { Aegis, AccountImportFormat.AegisJson },
        { TwoFas, AccountImportFormat.TwoFasJson },
        { JsonSerializer.Serialize(new[] { new Account(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Native", Seed, "user") }), AccountImportFormat.OtpHarborJson },
        { "id,issuer,account_name,secret\n00000000-0000-0000-0000-000000000001,Native,user," + Seed, AccountImportFormat.OtpHarborCsv },
        { "issuer|account_name|secret|id\nNative|user|" + Seed + "|00000000-0000-0000-0000-000000000001", AccountImportFormat.OtpHarborText },
        { "TOTP|user|" + Seed + "|00000000-0000-0000-0000-000000000001", AccountImportFormat.OtpHarborText },
        { UriValue, AccountImportFormat.TotpUriList },
        { JsonSerializer.Serialize(new { encrypted = false, items = new[] { new { type = 1, name = "Demo", login = new { totp = UriValue } } } }), AccountImportFormat.BitwardenJson },
        { "\"name\",\"type\",\"login_totp\"\nDemo,login," + Seed, AccountImportFormat.BitwardenCsv },
        { JsonSerializer.Serialize(new { version = 1, entries = new[] { new { content = new { entry_type = "Totp", uri = UriValue } } } }), AccountImportFormat.ProtonJson }
    };

    [Theory]
    [MemberData(nameof(Supported))]
    public async Task SupportedExports_AreDetectedByContentsWithoutAskingForASource(string content, AccountImportFormat expected)
    {
        foreach (var name in new[] { "renamed.json", "renamed.csv", "renamed.2fas", "renamed.otphbp", "no-extension" })
        {
            await using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(content));
            var asked = false;
            var result = await _service.ImportFromStreamWithFormatSelectionAsync(stream, name, (_, _) =>
            {
                asked = true;
                return Task.FromResult<AccountImportFormat?>(null);
            }, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.False(asked);
            Assert.Equal(Seed, Assert.Single(result.Value).Secret);
            Assert.Equal(expected, Assert.Single(result.Successes.OfType<AccountImportReadSummary>()).Format);
        }
    }

    [Theory]
    [InlineData(AccountImportFormat.AegisJson)]
    [InlineData(AccountImportFormat.TwoFasJson)]
    public async Task AmbiguousExport_UsesOnlyTheExplicitlyChosenRecognizedParser(AccountImportFormat selected)
    {
        await using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(Ambiguous()));
        var asked = 0;
        var result = await _service.ImportFromStreamWithFormatSelectionAsync(stream, "renamed.txt", (formats, _) =>
        {
            asked++;
            Assert.Equal(2, formats.Count);
            Assert.Contains(AccountImportFormat.AegisJson, formats);
            Assert.Contains(AccountImportFormat.TwoFasJson, formats);
            return Task.FromResult<AccountImportFormat?>(selected);
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, asked);
        Assert.Equal(selected == AccountImportFormat.AegisJson ? "Aegis" : "2FAS", Assert.Single(result.Value).Issuer);
    }

    [Fact]
    public async Task AmbiguousExport_WithoutASelectionOrWithAnUnrecognizedSelection_FailsSafely()
    {
        foreach (var selected in new AccountImportFormat?[] { null, AccountImportFormat.ProtonJson })
        {
            await using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(Ambiguous()));
            var result = await _service.ImportFromStreamWithFormatSelectionAsync(stream, "ambiguous.json",
                (_, _) => Task.FromResult(selected), TestContext.Current.CancellationToken);
            Assert.True(result.IsFailed);
        }
        await using var noChoice = new MemoryStream(Encoding.UTF8.GetBytes(Ambiguous()));
        Assert.True((await _service.ImportFromStreamAsync(noChoice, "ambiguous.json", cancellationToken: TestContext.Current.CancellationToken)).IsFailed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"unrecognized\":[]}")]
    [InlineData("{\"encrypted\":true,\"items\":[]}")]
    [InlineData("{\"version\":1,\"salt\":\"synthetic\",\"encryptedData\":\"synthetic\"}")]
    [InlineData("{\"encrypted\":true,\"encrypted\":false,\"items\":[]}")]
    [InlineData("name,type,login_totp\n\"unterminated")]
    public async Task MalformedUnknownAndEncryptedExports_NeverPromptForAnArbitraryParser(string content)
    {
        await using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(content));
        var asked = false;
        var result = await _service.ImportFromStreamWithFormatSelectionAsync(stream, "file.json", (_, _) =>
        { asked = true; return Task.FromResult<AccountImportFormat?>(AccountImportFormat.AegisJson); }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
        Assert.False(asked);
    }

    [Fact]
    public async Task RenamedBinaryOtpHarborBackup_RequiresSeparateRestoreWithoutPlaintextParsing()
    {
        var bytes = new byte[100];
        Encoding.ASCII.GetBytes("TOTP").CopyTo(bytes, 0);
        await using var stream = new NonSeekableStream(bytes);
        var asked = false;
        var result = await _service.ImportFromStreamWithFormatSelectionAsync(stream, "renamed.json", (_, _) =>
        { asked = true; return Task.FromResult<AccountImportFormat?>(AccountImportFormat.OtpHarborJson); }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
        Assert.False(asked);
        Assert.Equal(AccountImportFormat.EncryptedOtpHarborBackup, Assert.Single(result.Errors.OfType<AccountImportReadError>()).Summary.Format);
    }

    private static string Ambiguous()
    {
        var json = JsonNode.Parse(Aegis)!.AsObject();
        json["schemaVersion"] = 4;
        json["services"] = JsonNode.Parse(TwoFas)!["services"]!.DeepClone();
        return json.ToJsonString();
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
