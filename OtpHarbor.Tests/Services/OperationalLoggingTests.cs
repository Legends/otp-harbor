using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Logging;
using TOTP.Infrastructure.Parser;
using TOTP.Infrastructure.Services;
using TOTP.Tests.TestData;

namespace TOTP.Tests.Services;

public sealed class OperationalLoggingTests
{
    [Theory]
    [InlineData(AppLogLevel.Verbose)]
    [InlineData(AppLogLevel.Debug)]
    [InlineData(AppLogLevel.Information)]
    [InlineData(AppLogLevel.Warning)]
    [InlineData(AppLogLevel.Error)]
    [InlineData(AppLogLevel.Fatal)]
    public void FilePipeline_HonorsEveryConfiguredLevel(AppLogLevel minimum)
    {
        var root = Path.Combine(Path.GetTempPath(), $"otp-level-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "app.log");
            using (var sink = new LoggerConfiguration().MinimumLevel.Is(minimum.ToSerilogLevel())
                .WriteTo.Async(write => write.File(new RedactingTextFormatter("{Level:u3} {Message:lj}{NewLine}"),
                    path, rollingInterval: RollingInterval.Day, shared: true)).CreateLogger())
            {
                using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddSerilog(sink));
                var logger = factory.CreateLogger("SyntheticDiagnosticTest");
                for (var level = 0; level <= 5; level++)
                    logger.Log((LogLevel)level, "Synthetic level event {Index}.", level);
            }
            var text = File.ReadAllText(Assert.Single(Directory.EnumerateFiles(root, "*.log")));
            for (var level = 0; level <= 5; level++)
                Assert.Equal(level >= (int)minimum, text.Contains($"Synthetic level event {level}.", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GoogleQrImport_TenAccounts_CreatesShareableLogWithoutAccountOrQrData()
    {
        var root = Path.Combine(Path.GetTempPath(), $"otp-import-log-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "app.log");
            using (var sink = new LoggerConfiguration().MinimumLevel.Verbose()
                .WriteTo.Async(write => write.File(new RedactingTextFormatter("{Level:u3} {Message:lj}{NewLine}"),
                    path, rollingInterval: RollingInterval.Day, shared: true)).CreateLogger())
            {
                using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddSerilog(sink));
                var accounts = new Mock<IAccountManager>();
                accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
                    .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
                accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
                accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>())).ReturnsAsync(Result.Ok());
                var service = new QrAccountImportService(accounts.Object, factory.CreateLogger<QrAccountImportService>());
                var result = await service.ImportWithConflictResolutionAsync(
                    GoogleAuthenticatorMigrationTestData.TenAccountPayload,
                    (_, _) => Task.FromResult<QrAccountImportResolution?>(new([])), TestContext.Current.CancellationToken);
                Assert.True(result.IsSuccess);
                Assert.Equal(10, result.Value.ImportedCount);
            }
            var file = Assert.Single(Directory.EnumerateFiles(root, "app*.log"));
            var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            Assert.Contains("DBG", text);
            Assert.Contains("VRB", text);
            Assert.Contains("INF", text);
            Assert.Contains("imported: 10", text);
            Assert.DoesNotContain(GoogleAuthenticatorMigrationTestData.TenAccountPayload, text);
            foreach (var account in GoogleAuthenticatorMigrationParser.Parse(GoogleAuthenticatorMigrationTestData.TenAccountPayload).Accounts)
            {
                Assert.DoesNotContain(account.SecretBase32, text);
                Assert.DoesNotContain(account.Label, text);
                Assert.DoesNotContain(account.Issuer!, text);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ImportFailure_DoesNotLogRawResultErrors()
    {
        var output = new RecordingSink();
        using var sink = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(output).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddSerilog(sink));
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Fail<IReadOnlyList<Account>>("private@example.test JBSWY3DPEHPK3PXP"));
        var service = new AccountImportService(accounts.Object, factory.CreateLogger<AccountImportService>());
        var result = await service.ImportAsync(
            [new Account(Guid.NewGuid(), "Sensitive issuer", "JBSWY3DPEHPK3PXP", "private@example.test")],
            ImportConflictStrategy.SkipExisting, (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);
        Assert.Equal(AccountImportStatus.ExistingAccountsUnavailable, result.Value.Status);
        Assert.Contains(output.Events, item => item.Level == Serilog.Events.LogEventLevel.Warning);
        var text = string.Join("\n", output.Events.Select(item => item.RenderMessage()));
        Assert.DoesNotContain("private@example.test", text);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", text);
        Assert.DoesNotContain("Sensitive issuer", text);
    }

    private sealed class RecordingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];
        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }
}
