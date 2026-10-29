using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Common;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class ExportServiceFuzzTests
{
    private readonly ExportService _sut = new(NullLogger<ExportService>.Instance);

    [Fact]
    public async Task ImportFromFileAsync_FuzzMalformedJson_DoesNotThrow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var rng = new Random(1337);

        for (var i = 0; i < 75; i++)
        {
            var path = Path.Combine(temp.Path, $"fuzz-{i}.json");
            var payload = BuildMalformedJsonCandidate(rng, i);
            await File.WriteAllTextAsync(path, payload, cancellationToken);

            var result = await _sut.ImportFromFileAsync(path);

            if (result.IsFailed)
            {
                Assert.Contains(result.Errors, e =>
                    e is AppError appError &&
                    (appError.Code == AppErrorCode.ImportInvalidPayload ||
                     appError.Code == AppErrorCode.ImportUnknownFailed));
            }
        }
    }

    [Fact]
    public async Task ImportFromFileAsync_FuzzCsvAndTxt_DoesNotThrow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var rng = new Random(4242);

        for (var i = 0; i < 100; i++)
        {
            var csvPath = Path.Combine(temp.Path, $"fuzz-{i}.csv");
            var txtPath = Path.Combine(temp.Path, $"fuzz-{i}.txt");
            await File.WriteAllTextAsync(csvPath, BuildMalformedDelimitedPayload(rng, ',', i), cancellationToken);
            await File.WriteAllTextAsync(txtPath, BuildMalformedDelimitedPayload(rng, '|', i), cancellationToken);

            var csvResult = await _sut.ImportFromFileAsync(csvPath);
            var txtResult = await _sut.ImportFromFileAsync(txtPath);

            Assert.True(csvResult.IsSuccess || csvResult.IsFailed);
            Assert.True(txtResult.IsSuccess || txtResult.IsFailed);
        }
    }

    [Fact]
    public async Task ImportFromEncryptedFileAsync_FuzzRandomBinary_DoesNotThrowAndFailsSafely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var rng = new Random(9001);

        for (var i = 0; i < 100; i++)
        {
            var path = Path.Combine(temp.Path, $"fuzz-{i}.otphbp");
            var bytes = new byte[rng.Next(1, 2048)];
            rng.NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);

            var result = await _sut.ImportFromEncryptedFileAsync("pw", path);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e =>
                e is AppError appError &&
                (appError.Code == AppErrorCode.ImportInvalidFile ||
                 appError.Code == AppErrorCode.ImportWrongPasswordOrTampered ||
                 appError.Code == AppErrorCode.ImportUnknownFailed));
        }
    }

    [Fact]
    public async Task ImportFromFileAsync_FuzzTotpExtension_WithRandomBinary_DoesNotThrowAndFailsSafely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var rng = new Random(1776);

        for (var i = 0; i < 50; i++)
        {
            var path = Path.Combine(temp.Path, $"fuzz-{i}.otphbp");
            var bytes = new byte[rng.Next(1, 1024)];
            rng.NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);

            var result = await _sut.ImportFromFileAsync(path, "pw");

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, e =>
                e is AppError appError &&
                (appError.Code == AppErrorCode.ImportInvalidFile ||
                 appError.Code == AppErrorCode.ImportWrongPasswordOrTampered ||
                 appError.Code == AppErrorCode.ImportUnknownFailed));
        }
    }

    private static string BuildMalformedJsonCandidate(Random rng, int iteration)
    {
        var fragments = new[]
        {
            "{",
            "{\"id\":\"" + Guid.NewGuid() + "\",\"issuer\":",
            "[{\"issuer\":\"GitHub\",\"secret\":\"AAAA\"}",
            "\"unterminated",
            new string((char)0xFFFD, rng.Next(1, 10)),
            "{not-json}",
            "{\"issuer\":\"ok\",\"secret\":\"" + new string('A', rng.Next(1, 80)) + "\""
        };

        return string.Concat(fragments[iteration % fragments.Length], new string('x', rng.Next(0, 40)));
    }

    private static string BuildMalformedDelimitedPayload(Random rng, char delimiter, int iteration)
    {
        var lines = new List<string>
        {
            delimiter == ',' ? "id,issuer,account_name,secret" : "issuer|account_name|secret|id"
        };

        var rowCount = rng.Next(1, 8);
        for (var i = 0; i < rowCount; i++)
        {
            var tokenA = RandomToken(rng, rng.Next(0, 20), includeDelimiters: true);
            var tokenB = RandomToken(rng, rng.Next(0, 20), includeDelimiters: true);
            var tokenC = RandomToken(rng, rng.Next(0, 40), includeDelimiters: true);

            if (delimiter == ',')
            {
                lines.Add($"{Guid.NewGuid()},{tokenA},{tokenB},{tokenC}");
            }
            else
            {
                var maybeId = iteration % 3 == 0 ? Guid.NewGuid().ToString() : RandomToken(rng, 8, includeDelimiters: false);
                lines.Add($"{tokenA}|{tokenB}|{tokenC}|{maybeId}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string RandomToken(Random rng, int length, bool includeDelimiters)
    {
        const string basic = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        const string extra = "\",|\n\r";
        var alphabet = includeDelimiters ? basic + extra : basic;
        var chars = new char[length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[rng.Next(alphabet.Length)];
        }

        return new string(chars);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "totp-fuzz-tests-" + Guid.NewGuid().ToString("N"));
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
}
