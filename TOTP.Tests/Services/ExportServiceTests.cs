using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace TOTP.Tests.Services;

public sealed class ExportServiceTests
{
    private readonly ExportService _sut = new(NullLogger<ExportService>.Instance);

    [Theory]
    [InlineData(ExportFileFormat.Json, ".json")]
    [InlineData(ExportFileFormat.Txt, ".txt")]
    [InlineData(ExportFileFormat.Csv, ".csv")]
    public async Task ExportToFileAsync_ThenImportFromFileAsync_RoundTripsSupportedFormats(ExportFileFormat format, string extension)
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "accounts" + extension);
        var id = Guid.NewGuid();
        List<Account> input =
        [
            new(id, "GitHub, Inc.", "AAAABBBB", "john\"doe", 60),
            new(Guid.NewGuid(), "Google", "CCCCDDDD")
        ];

        var export = await _sut.ExportToFileAsync(input, path, format);
        var import = await _sut.ImportFromFileAsync(path);

        Assert.True(export.IsSuccess);
        Assert.True(import.IsSuccess);
        Assert.Equal(2, import.Value.Count);
        Assert.Equal(id, import.Value[0].ID);
        Assert.Equal("GitHub, Inc.", import.Value[0].Issuer);
        Assert.Equal("AAAABBBB", import.Value[0].Secret);
        Assert.Equal("john\"doe", import.Value[0].AccountName);
        Assert.Equal(60, import.Value[0].PeriodSeconds);
    }

    [Fact]
    public async Task ImportFromFileAsync_WhenExtensionUnsupported_ReturnsInvalidFileError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "accounts.xml");
        await File.WriteAllTextAsync(path, "<accounts/>", cancellationToken);

        var result = await _sut.ImportFromFileAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidFile, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromFileAsync_WhenFileMissing_ReturnsFileNotFoundError()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "missing.json");

        var result = await _sut.ImportFromFileAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportFileNotFound, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromFileAsync_WhenFileTooLarge_ReturnsInvalidFileError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "oversized.json");
        var oversized = new string('A', 6 * 1024 * 1024);
        await File.WriteAllTextAsync(path, oversized, cancellationToken);

        var result = await _sut.ImportFromFileAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidFile, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromFileAsync_WhenEncryptedWithoutPassword_ReturnsWrongPasswordError()
    {
        using var temp = new TempDir();
        var encrypted = Path.Combine(temp.Path, "accounts.totp");
        var export = await _sut.ExportToEncryptedFileAsync(
            [new Account(Guid.NewGuid(), "GitHub", "SECRET")],
            "correct-password",
            encrypted,
            ExportFileFormat.Json);
        Assert.True(export.IsSuccess);

        var result = await _sut.ImportFromFileAsync(encrypted, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportWrongPasswordOrTampered, result.GetErrorCode());
    }

    [Theory]
    [InlineData(ExportFileFormat.Json)]
    [InlineData(ExportFileFormat.Txt)]
    [InlineData(ExportFileFormat.Csv)]
    public async Task ExportToEncryptedFileAsync_ThenImportFromEncryptedFileAsync_RoundTrips(ExportFileFormat format)
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "accounts.totp");
        List<Account> input = [new(Guid.NewGuid(), "Azure", "ABCD1234", "tenant-user", 60)];

        var export = await _sut.ExportToEncryptedFileAsync(input, "pw-123", path, format);
        var import = await _sut.ImportFromEncryptedFileAsync("pw-123", path);

        Assert.True(export.IsSuccess);
        Assert.True(import.IsSuccess);
        var token = Assert.Single(import.Value);
        Assert.Equal("Azure", token.Issuer);
        Assert.Equal("ABCD1234", token.Secret);
        Assert.Equal("tenant-user", token.AccountName);
        Assert.Equal(60, token.PeriodSeconds);
    }

    [Theory]
    [InlineData("legacy.json", "[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"issuer\":\"Legacy\",\"secret\":\"AAAABBBB\",\"account_name\":\"alice\"}]")]
    [InlineData("legacy.txt", "issuer|account_name|secret|id\nLegacy|alice|AAAABBBB|00000000-0000-0000-0000-000000000001")]
    [InlineData("legacy.csv", "id,issuer,account_name,secret\n00000000-0000-0000-0000-000000000001,Legacy,alice,AAAABBBB")]
    public async Task ImportFromStreamAsync_WhenLegacyFormatHasNoPeriod_UsesDefault(
        string fileName,
        string content)
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            fileName,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, Assert.Single(result.Value).PeriodSeconds);
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithOtpAuthTextFile_ImportsStandardTotpUris()
    {
        const string content = """
            # Exported TOTP accounts

            otpauth://totp/GitHub%3Aalice%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
            otpauth://totp/AWS%3Aproduction?secret=KRSXG5DSNFXGOIDB&issuer=Amazon%20Web%20Services&period=60
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "authenticator-export.txt",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            github =>
            {
                Assert.Equal("GitHub", github.Issuer);
                Assert.Equal("alice@example.test", github.AccountName);
                Assert.Equal("JBSWY3DPEHPK3PXP", github.Secret);
                Assert.Equal(30, github.PeriodSeconds);
            },
            aws =>
            {
                Assert.Equal("Amazon Web Services", aws.Issuer);
                Assert.Equal("production", aws.AccountName);
                Assert.Equal("KRSXG5DSNFXGOIDB", aws.Secret);
                Assert.Equal(60, aws.PeriodSeconds);
            });
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithPlaintextAegisVault_ImportsSupportedTotpEntries()
    {
        const string content = """
            {
              "version": 1,
              "header": { "slots": null, "params": null },
              "db": {
                "version": 3,
                "entries": [
                  {
                    "type": "totp",
                    "uuid": "00000000-0000-0000-0000-000000000001",
                    "name": " alice@example.test ",
                    "issuer": " GitHub ",
                    "info": {
                      "secret": "jbsw y3dp-ehpk3pxp====",
                      "algo": "SHA1",
                      "digits": 6,
                      "period": 60
                    }
                  }
                ],
                "groups": []
              }
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "aegis-export.json",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("GitHub", account.Issuer);
        Assert.Equal("alice@example.test", account.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", account.Secret);
        Assert.Equal(60, account.PeriodSeconds);
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithEncryptedAegisVault_FailsClosed()
    {
        const string content = """
            {
              "version": 1,
              "header": { "slots": [], "params": { "nonce": "00", "tag": "00" } },
              "db": "encrypted-base64-payload"
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "encrypted-aegis.json",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithPlaintextTwoFasBackup_ImportsSupportedTotpServices()
    {
        const string content = """
            {
              "services": [
                {
                  "name": "GitHub display name",
                  "secret": "jbsw y3dp-ehpk3pxp====",
                  "updatedAt": 1,
                  "serviceTypeID": null,
                  "otp": {
                    "link": null,
                    "label": "fallback@example.test",
                    "account": "alice@example.test",
                    "issuer": "GitHub",
                    "digits": 6,
                    "period": 60,
                    "algorithm": "SHA1",
                    "counter": null,
                    "tokenType": "TOTP",
                    "source": "Manual"
                  },
                  "order": { "position": 0 },
                  "badge": null,
                  "icon": null,
                  "groupId": null
                }
              ],
              "groups": [],
              "updatedAt": 1,
              "schemaVersion": 4,
              "appVersionCode": 1,
              "appVersionName": "test",
              "appOrigin": "android",
              "servicesEncrypted": null,
              "reference": null
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "twofas-backup.2fas",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("GitHub", account.Issuer);
        Assert.Equal("alice@example.test", account.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", account.Secret);
        Assert.Equal(60, account.PeriodSeconds);
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithLegacyPlaintextTwoFasDefaults_UsesServiceNameAndTotpDefaults()
    {
        const string content = """
            {
              "services": [
                {
                  "name": "Example",
                  "secret": "JBSWY3DPEHPK3PXP",
                  "otp": { "label": "bob@example.test" }
                }
              ],
              "schemaVersion": 2,
              "servicesEncrypted": null,
              "reference": null
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "legacy.2fas",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value);
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("bob@example.test", account.AccountName);
        Assert.Equal(30, account.PeriodSeconds);
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithEncryptedTwoFasBackup_FailsClosed()
    {
        const string content = """
            {
              "services": [],
              "schemaVersion": 4,
              "servicesEncrypted": "encrypted-services",
              "reference": "encrypted-reference"
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "encrypted.2fas",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }

    [Theory]
    [InlineData("HOTP", "SHA1", 6, 30)]
    [InlineData("TOTP", "SHA256", 6, 30)]
    [InlineData("TOTP", "SHA1", 8, 30)]
    [InlineData("TOTP", "SHA1", 6, 3601)]
    public async Task ImportFromStreamAsync_WithUnsupportedTwoFasService_FailsWholeImport(
        string tokenType,
        string algorithm,
        int digits,
        int period)
    {
        var content = $$"""
            {
              "services": [
                {
                  "name": "Example",
                  "secret": "JBSWY3DPEHPK3PXP",
                  "otp": {
                    "account": "alice",
                    "issuer": "Example",
                    "tokenType": "{{tokenType}}",
                    "algorithm": "{{algorithm}}",
                    "digits": {{digits}},
                    "period": {{period}}
                  }
                }
              ],
              "schemaVersion": 4,
              "servicesEncrypted": null,
              "reference": null
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "unsupported.2fas",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }

    [Theory]
    [InlineData("hotp", "SHA1", 6, 30)]
    [InlineData("totp", "SHA256", 6, 30)]
    [InlineData("totp", "SHA1", 8, 30)]
    [InlineData("totp", "SHA1", 6, 4)]
    public async Task ImportFromStreamAsync_WithUnsupportedAegisEntry_FailsWholeImport(
        string type,
        string algorithm,
        int digits,
        int period)
    {
        var content = $$"""
            {
              "version": 1,
              "header": { "slots": null, "params": null },
              "db": {
                "version": 3,
                "entries": [
                  {
                    "type": "totp",
                    "name": "valid",
                    "issuer": "Example",
                    "info": { "secret": "JBSWY3DPEHPK3PXP", "algo": "SHA1", "digits": 6, "period": 30 }
                  },
                  {
                    "type": "{{type}}",
                    "name": "unsupported",
                    "issuer": "Example",
                    "info": { "secret": "KRSXG5DSNFXGOIDB", "algo": "{{algorithm}}", "digits": {{digits}}, "period": {{period}} }
                  }
                ],
                "groups": []
              }
            }
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "aegis-export.json",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }

    [Theory]
    [InlineData("otpauth://hotp/Example%3Aalice?secret=JBSWY3DPEHPK3PXP&issuer=Example&counter=1")]
    [InlineData("otpauth://totp/Example%3Aalice?secret=INVALID1&issuer=Example")]
    [InlineData("otpauth://totp/Example%3Aalice?secret=JBSWY3DPEHPK3PXP&issuer=Example&algorithm=SHA256")]
    [InlineData("otpauth://totp/Example%3Aalice?secret=JBSWY3DPEHPK3PXP&issuer=Example&digits=8")]
    [InlineData("otpauth://totp/Example%3Aalice?secret=JBSWY3DPEHPK3PXP&issuer=Example&period=invalid")]
    public async Task ImportFromStreamAsync_WithInvalidOrUnsupportedOtpAuthUri_FailsClosed(
        string content)
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "accounts.txt",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromStreamAsync_WithMixedOtpAuthAndLegacyRows_RejectsWholeFile()
    {
        const string content = """
            otpauth://totp/GitHub%3Aalice?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
            Example|bob|KRSXG5DSNFXGOIDB
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _sut.ImportFromStreamAsync(
            stream,
            "mixed.txt",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }

    [Fact]
    public async Task ExportToEncryptedStreamAsync_ThenPathImporter_RoundTripsCompatibilityFormat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "stream-generated.totp");
        var input = new Account(Guid.NewGuid(), "GitHub", "ABCD1234", "stream-user");

        await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
        {
            var export = await _sut.ExportToEncryptedStreamAsync(
                [input], "pw-123", destination, ExportFileFormat.Json, cancellationToken);
            Assert.True(export.IsSuccess);
            Assert.True(destination.CanWrite);
        }

        var import = await _sut.ImportFromEncryptedFileAsync("pw-123", path);

        Assert.True(import.IsSuccess);
        var account = Assert.Single(import.Value);
        Assert.Equal(input.ID, account.ID);
        Assert.Equal(input.Secret, account.Secret);
    }

    [Fact]
    public async Task PathExporter_ThenNonSeekableStreamImporter_RoundTripsCompatibilityFormat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "path-generated.totp");
        var input = new Account(Guid.NewGuid(), "GitLab", "EFGH5678", "path-user");
        Assert.True((await _sut.ExportToEncryptedFileAsync(
            [input], "pw-456", path, ExportFileFormat.Json)).IsSuccess);

        await using var file = File.OpenRead(path);
        await using var source = new NonSeekableReadStream(file);
        var import = await _sut.ImportFromStreamAsync(source, "portable.totp", "pw-456", cancellationToken);

        Assert.True(import.IsSuccess);
        var account = Assert.Single(import.Value);
        Assert.Equal(input.ID, account.ID);
        Assert.Equal(input.Secret, account.Secret);
        Assert.False(source.CanSeek);
    }

    [Fact]
    public async Task ImportFromStreamAsync_WhenNonSeekableStreamExceedsLimit_ReturnsInvalidFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = new NonSeekableReadStream(
            new MemoryStream(new byte[(5 * 1024 * 1024) + 1], writable: false));

        var result = await _sut.ImportFromStreamAsync(source, "oversized.json", cancellationToken: cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidFile, result.GetErrorCode());
    }

    [Theory]
    [InlineData("accounts.JSON")]
    [InlineData("accounts.2FAS")]
    [InlineData("accounts.txt")]
    [InlineData("accounts.CsV")]
    public async Task ImportFromStreamAsync_UsesPortableFileNameExtension(string fileName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var format = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" or ".2fas" => ExportFileFormat.Json,
            ".txt" => ExportFileFormat.Txt,
            _ => ExportFileFormat.Csv
        };
        await using var stream = new MemoryStream();
        Assert.True((await _sut.ExportToStreamAsync(
            [new Account(Guid.NewGuid(), "Issuer", "SECRET", "user")], stream, format, cancellationToken)).IsSuccess);
        stream.Position = 0;

        var result = await _sut.ImportFromStreamAsync(stream, fileName, cancellationToken: cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
    }

    [Fact]
    public async Task ImportFromEncryptedFileAsync_WhenPasswordWrong_ReturnsWrongPasswordOrTampered()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "accounts.totp");
        Assert.True((await _sut.ExportToEncryptedFileAsync(
            [new Account(Guid.NewGuid(), "Google", "XYZ")],
            "right-password",
            path,
            ExportFileFormat.Json)).IsSuccess);

        var result = await _sut.ImportFromEncryptedFileAsync("wrong-password", path);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportWrongPasswordOrTampered, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromEncryptedFileAsync_WhenHeaderInvalid_ReturnsInvalidFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "invalid.totp");
        await File.WriteAllBytesAsync(path, "not-a-valid-header"u8.ToArray(), cancellationToken);

        var result = await _sut.ImportFromEncryptedFileAsync("pw", path);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidFile, result.GetErrorCode());
    }

    [Fact]
    public async Task ImportFromEncryptedFileAsync_WhenFileTooLarge_ReturnsInvalidFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "oversized.totp");
        await File.WriteAllBytesAsync(path, new byte[6 * 1024 * 1024], cancellationToken);

        var result = await _sut.ImportFromEncryptedFileAsync("pw", path);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ImportInvalidFile, result.GetErrorCode());
    }

    [Fact]
    public async Task ExportToEncryptedFileAsync_WhenDirectoryMissing_ReturnsWriteFailed()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "missing", "accounts.totp");

        var result = await _sut.ExportToEncryptedFileAsync(
            [new Account(Guid.NewGuid(), "GitHub", "SECRET")],
            "pw",
            path,
            ExportFileFormat.Json);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ExportFileWriteFailed, result.GetErrorCode());
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "totp-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best-effort test cleanup
            }
        }
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
